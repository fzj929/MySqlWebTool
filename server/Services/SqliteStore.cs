using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace DataPilot.Api.Services;

public sealed record SqliteFile(string Id, string Name, long Size, DateTime UploadedAt);

/// <summary>Managed SQLite files are private data, never static web assets.</summary>
public sealed class SqliteStore
{
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public string Root { get; }
    public long MaxBytes { get; }
    private readonly long _maxTotalBytes;

    public SqliteStore(IConfiguration config, IWebHostEnvironment env)
    {
        Root = Path.GetFullPath(config["DataPilot:SqliteDirectory"] ??
            Path.Combine(env.ContentRootPath, "..", "DataPilot-data"));
        Directory.CreateDirectory(Root);
        MaxBytes = config.GetValue<long?>("DataPilot:MaxUploadBytes") ?? 104857600;
        _maxTotalBytes = config.GetValue<long?>("DataPilot:MaxStorageBytes") ?? 1073741824;
        var webRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot")));
        if (Path.TrimEndingDirectorySeparator(Root).Equals(webRoot, StringComparison.OrdinalIgnoreCase) ||
            Root.StartsWith(webRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SQLite 数据不能存储在 wwwroot 内");
    }

    public string FilePath(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("无效的文件 ID");
        var path = Path.Combine(Root, id + ".db");
        if (!File.Exists(path)) throw new ArgumentException("数据库文件不存在或已删除，请重新上传");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("不允许符号链接");
        return path;
    }

    public List<SqliteFile> List() => Directory.EnumerateFiles(Root, "*.json")
        .Select(p => JsonSerializer.Deserialize<SqliteFile>(File.ReadAllText(p)))
        .Where(x => x != null && File.Exists(Path.Combine(Root, x.Id + ".db")))
        .Select(x => x! with { Size = new FileInfo(FilePath(x!.Id)).Length })
        .OrderByDescending(x => x.UploadedAt).ToList();

    public static SqliteConnection Open(string path, bool readOnly = false)
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource = path, Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
            Pooling = false, DefaultTimeout = 10
        }.ToString());
        try
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA trusted_schema=OFF; PRAGMA foreign_keys=ON; PRAGMA mmap_size=0;";
            cmd.ExecuteNonQuery();
            raw.sqlite3_limit(c.Handle, raw.SQLITE_LIMIT_ATTACHED, 0);
            raw.sqlite3_limit(c.Handle, raw.SQLITE_LIMIT_LENGTH, 16777216);
            raw.sqlite3_limit(c.Handle, raw.SQLITE_LIMIT_SQL_LENGTH, 1048576);
            raw.sqlite3_set_authorizer(c.Handle, (_, action, p0, p1, db, trigger) =>
            {
                if (action == raw.SQLITE_ATTACH || action == raw.SQLITE_DETACH) return raw.SQLITE_DENY;
                var name = p0.utf8_to_string();
                if (action == raw.SQLITE_PRAGMA && name?.ToLowerInvariant() is
                    "writable_schema" or "trusted_schema" or "temp_store_directory" or "data_store_directory" or "journal_mode")
                    return raw.SQLITE_DENY;
                if (action == raw.SQLITE_FUNCTION && p1.utf8_to_string()?.ToLowerInvariant() is "load_extension" or "readfile" or "writefile")
                    return raw.SQLITE_DENY;
                return raw.SQLITE_OK;
            }, null);
            return c;
        }
        catch { c.Dispose(); throw; }
    }

    public async Task<SqliteFile> Upload(IFormFile file, CancellationToken ct)
    {
        if (file.Length < 100 || file.Length > MaxBytes) throw new ArgumentException($"文件大小必须为 100 字节至 {MaxBytes / 1048576} MB");
        if (!new[] { ".db", ".sqlite", ".sqlite3" }.Contains(Path.GetExtension(file.FileName).ToLowerInvariant()))
            throw new ArgumentException("支持 .db、.sqlite、.sqlite3 文件");
        await Gate.WaitAsync(ct);
        var id = Guid.NewGuid().ToString("N");
        var path = Path.Combine(Root, id + ".db");
        try
        {
            if (Directory.EnumerateFiles(Root).Sum(p => new FileInfo(p).Length) + file.Length > _maxTotalBytes)
                throw new ArgumentException("SQLite 存储配额已用尽，请删除不用的数据库");
            await using (var input = file.OpenReadStream())
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            {
                var header = new byte[16];
                await input.ReadExactlyAsync(header, ct);
                if (!header.SequenceEqual(Encoding.ASCII.GetBytes("SQLite format 3\0"))) throw new ArgumentException("不是有效的 SQLite 3 数据库；加密库不受支持");
                await output.WriteAsync(header, ct);
                var buffer = new byte[81920];
                long size = 16;
                int count;
                while ((count = await input.ReadAsync(buffer, ct)) > 0)
                {
                    size += count;
                    if (size > MaxBytes) throw new ArgumentException("数据库超过上传上限");
                    await output.WriteAsync(buffer.AsMemory(0, count), ct);
                }
            }
            using (var c = Open(path, true))
            {
                var until = DateTime.UtcNow.AddSeconds(30);
                raw.sqlite3_progress_handler(c.Handle, 10000, _ => ct.IsCancellationRequested || DateTime.UtcNow > until ? 1 : 0, null);
                using var cmd = c.CreateCommand();
                cmd.CommandText = "PRAGMA quick_check";
                if (Convert.ToString(cmd.ExecuteScalar()) != "ok") throw new ArgumentException("数据库完整性检查失败");
            }
            var item = new SqliteFile(id, Path.GetFileName(file.FileName.Replace('\\', '/')), new FileInfo(path).Length, DateTime.UtcNow);
            await File.WriteAllTextAsync(Path.Combine(Root, id + ".json"), JsonSerializer.Serialize(item), ct);
            return item;
        }
        catch
        {
            File.Delete(path);
            File.Delete(Path.Combine(Root, id + ".json"));
            throw;
        }
        finally { Gate.Release(); }
    }

    public async Task Delete(string id, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var path = FilePath(id);
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" }) File.Delete(path + suffix);
            File.Delete(Path.Combine(Root, id + ".json"));
        }
        finally { Gate.Release(); }
    }

    public async Task<(byte[] Bytes, string Name)> Download(string id, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        var temp = Path.Combine(Root, Guid.NewGuid().ToString("N") + ".snapshot");
        try
        {
            using (var source = Open(FilePath(id), true))
            using (var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temp, Pooling = false }.ToString()))
            {
                target.Open();
                source.BackupDatabase(target);
            }
            return (await File.ReadAllBytesAsync(temp, ct), List().First(x => x.Id == id).Name);
        }
        finally { File.Delete(temp); Gate.Release(); }
    }
}
