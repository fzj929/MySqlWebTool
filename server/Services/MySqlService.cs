using System.Data;
using System.Diagnostics;
using System.Text;
using MySqlConnector;
using MySqlTool.Api.Models;
using ConnectionInfo = MySqlTool.Api.Models.ConnectionInfo;

namespace MySqlTool.Api.Services;

public interface IMySqlService
{
    Task<object> TestAsync(ConnectionInfo info, CancellationToken ct);
    Task<List<DatabaseItem>> GetDatabasesAsync(ConnectionInfo info, CancellationToken ct);
    Task<List<TableItem>> GetTablesAsync(ConnectionInfo info, string database, CancellationToken ct);
    Task<TableSchemaResult> GetTableSchemaAsync(ConnectionInfo info, string database, string table, CancellationToken ct);
    Task<QueryResult> ExecuteAsync(ConnectionInfo info, string sql, int? limit, CancellationToken ct);
    Task<TableDataResult> GetTableDataAsync(TableDataRequest req, CancellationToken ct);
    Task<(byte[] Content, string FileName, int Rows)> ExportAsync(ConnectionInfo info, string sql, int? limit, CancellationToken ct);
}

public sealed class TableDataResult : QueryResult
{
    public long Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public sealed class MySqlService : IMySqlService
{
    private readonly int _maxRows;
    private readonly int _maxExportRows;
    private readonly int _commandTimeout;
    private readonly int _connectTimeout;

    public MySqlService(IConfiguration configuration)
    {
        var section = configuration.GetSection("MySqlTool");
        _maxRows = section.GetValue<int?>("MaxRows") ?? 1000;
        _maxExportRows = section.GetValue<int?>("MaxExportRows") ?? 50000;
        _commandTimeout = section.GetValue<int?>("CommandTimeoutSeconds") ?? 60;
        _connectTimeout = section.GetValue<int?>("ConnectTimeoutSeconds") ?? 10;
    }

    // ---------------------------------------------------------------- 连接

    private string BuildConnectionString(ConnectionInfo info)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = info.Host,
            Port = (uint)info.Port,
            UserID = info.User,
            Password = info.Password ?? "",
            SslMode = info.UseSsl ? MySqlSslMode.Required : MySqlSslMode.Preferred,
            CharacterSet = "utf8mb4",
            ConnectionTimeout = (uint)_connectTimeout,
            DefaultCommandTimeout = (uint)_commandTimeout,
            AllowUserVariables = true,
            AllowLoadLocalInfile = false,
            UseCompression = false,
        };

        if (!string.IsNullOrWhiteSpace(info.Database))
            builder.Database = info.Database;

        return builder.ConnectionString;
    }

    /// <summary>创建命令对象（返回 MySqlCommand 以便使用类型化参数集合）。</summary>
    private MySqlCommand CreateCommand(MySqlConnection connection)
    {
        var cmd = (MySqlCommand)connection.CreateCommand();
        cmd.CommandTimeout = _commandTimeout;
        return cmd;
    }

    private async Task<MySqlConnection> OpenAsync(ConnectionInfo info, CancellationToken ct)
    {
        info.Validate();
        var connection = new MySqlConnection(BuildConnectionString(info));
        try
        {
            await connection.OpenAsync(ct);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task<object> TestAsync(ConnectionInfo info, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        await using var connection = await OpenAsync(info, ct);

        string version = connection.ServerVersion ?? "";
        string? currentDb = null;
        await using (var cmd = CreateCommand(connection))
        {
            cmd.CommandText = "SELECT DATABASE()";
            currentDb = (await cmd.ExecuteScalarAsync(ct)) as string;
        }

        sw.Stop();
        return new
        {
            ok = true,
            serverVersion = version,
            currentDatabase = currentDb,
            elapsedMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2),
        };
    }

    // ---------------------------------------------------------------- 结构

    public async Task<List<DatabaseItem>> GetDatabasesAsync(ConnectionInfo info, CancellationToken ct)
    {
        await using var connection = await OpenAsync(info, ct);
        var items = new List<DatabaseItem>();

        await using var cmd = CreateCommand(connection);
        cmd.CommandText = """
            SELECT SCHEMA_NAME, DEFAULT_CHARACTER_SET_NAME
            FROM information_schema.SCHEMATA
            ORDER BY SCHEMA_NAME
            """;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            items.Add(new DatabaseItem
            {
                Name = reader.GetString(0),
                Charset = reader.IsDBNull(1) ? "" : reader.GetString(1),
            });
        }

        return items;
    }

    public async Task<List<TableItem>> GetTablesAsync(ConnectionInfo info, string database, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(database))
            throw new ArgumentException("数据库名不能为空");

        await using var connection = await OpenAsync(info, ct);
        var items = new List<TableItem>();

        await using var cmd = CreateCommand(connection);
        cmd.CommandText = """
            SELECT TABLE_NAME, TABLE_TYPE, ENGINE, TABLE_ROWS,
                   ROUND((DATA_LENGTH + INDEX_LENGTH) / 1024.0 / 1024.0, 2),
                   IFNULL(TABLE_COMMENT, '')
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = @db
            ORDER BY TABLE_TYPE, TABLE_NAME
            """;
        cmd.Parameters.AddWithValue("@db", database);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            items.Add(new TableItem
            {
                Name = reader.GetString(0),
                Type = reader.GetString(1),
                Engine = reader.IsDBNull(2) ? null : reader.GetString(2),
                Rows = reader.IsDBNull(3) ? null : reader.GetInt64(3),
                SizeMb = reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                Comment = reader.GetString(5),
            });
        }

        return items;
    }

    public async Task<TableSchemaResult> GetTableSchemaAsync(ConnectionInfo info, string database, string table, CancellationToken ct)
    {
        var result = new TableSchemaResult();
        await using var connection = await OpenAsync(info, ct);

        await using (var cmd = CreateCommand(connection))
        {
            cmd.CommandText = """
                SELECT ORDINAL_POSITION, COLUMN_NAME, COLUMN_TYPE, DATA_TYPE,
                       IS_NULLABLE, COLUMN_DEFAULT, COLUMN_KEY, EXTRA, COLUMN_COMMENT
                FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = @db AND TABLE_NAME = @tb
                ORDER BY ORDINAL_POSITION
                """;
            cmd.Parameters.AddWithValue("@db", database);
            cmd.Parameters.AddWithValue("@tb", table);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.Columns.Add(new ColumnInfo
                {
                    Ordinal = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    ColumnType = reader.GetString(2),
                    DataType = reader.GetString(3),
                    Nullable = string.Equals(reader.GetString(4), "YES", StringComparison.OrdinalIgnoreCase),
                    DefaultValue = reader.IsDBNull(5) ? null : reader.GetString(5),
                    Key = reader.GetString(6),
                    Extra = reader.GetString(7),
                    Comment = reader.GetString(8),
                });
            }
        }

        await using (var cmd = CreateCommand(connection))
        {
            cmd.CommandText = $"SHOW INDEX FROM {SqlIdentifier.Qualified(database, table)}";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.Indexes.Add(new IndexInfo
                {
                    Name = reader.GetString(reader.GetOrdinal("Key_name")),
                    Column = reader.GetString(reader.GetOrdinal("Column_name")),
                    Unique = reader.GetInt32(reader.GetOrdinal("Non_unique")) == 0,
                    Seq = reader.GetInt32(reader.GetOrdinal("Seq_in_index")),
                    Cardinality = reader.IsDBNull(reader.GetOrdinal("Cardinality"))
                        ? null
                        : reader.GetValue(reader.GetOrdinal("Cardinality")).ToString(),
                });
            }
        }

        // 把 SHOW INDEX 的扁平行聚合为索引定义（复合索引合并为一条）
        result.IndexDefinitions = result.Indexes
            .GroupBy(i => i.Name)
            .Select(g =>
            {
                var ordered = g.OrderBy(x => x.Seq).ToList();
                return new IndexDefinition
                {
                    Name = g.Key,
                    Primary = string.Equals(g.Key, "PRIMARY", StringComparison.OrdinalIgnoreCase),
                    Unique = ordered.First().Unique,
                    Columns = ordered.Select(x => x.Column).ToList(),
                };
            })
            .OrderBy(d => d.Primary ? 0 : 1)
            .ThenBy(d => d.Name)
            .ToList();

        // 表级元信息：引擎、字符集、注释、自增值
        await using (var cmd = CreateCommand(connection))
        {
            cmd.CommandText = """
                SELECT ENGINE, TABLE_COLLATION, TABLE_COMMENT, AUTO_INCREMENT, ROW_FORMAT
                FROM information_schema.TABLES
                WHERE TABLE_SCHEMA = @db AND TABLE_NAME = @tb
                """;
            cmd.Parameters.AddWithValue("@db", database);
            cmd.Parameters.AddWithValue("@tb", table);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                result.Meta = new TableMeta
                {
                    Engine = reader.IsDBNull(0) ? null : reader.GetString(0),
                    Collation = reader.IsDBNull(1) ? null : reader.GetString(1),
                    Comment = reader.IsDBNull(2) ? null : reader.GetString(2),
                    AutoIncrement = reader.IsDBNull(3) ? null : reader.GetInt64(3),
                    RowFormat = reader.IsDBNull(4) ? null : reader.GetString(4),
                };
            }
        }

        await using (var cmd = CreateCommand(connection))
        {
            cmd.CommandText = $"SHOW CREATE TABLE {SqlIdentifier.Qualified(database, table)}";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct) && reader.FieldCount > 1)
                result.Ddl = reader.IsDBNull(1) ? "" : reader.GetString(1);
        }

        return result;
    }

    // ---------------------------------------------------------------- 查询

    public async Task<QueryResult> ExecuteAsync(ConnectionInfo info, string sql, int? limit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sql))
            throw new ArgumentException("SQL 不能为空");

        await using var connection = await OpenAsync(info, ct);
        return await RunAsync(connection, sql, ClampLimit(limit, _maxRows), ct);
    }

    public async Task<TableDataResult> GetTableDataAsync(TableDataRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Database)) throw new ArgumentException("数据库名不能为空");
        if (string.IsNullOrWhiteSpace(req.Table)) throw new ArgumentException("表名不能为空");

        int page = Math.Max(1, req.Page);
        int pageSize = Math.Clamp(req.PageSize, 1, 1000);
        int offset = (page - 1) * pageSize;

        var qualified = SqlIdentifier.Qualified(req.Database, req.Table);
        string where = string.IsNullOrWhiteSpace(req.Where) ? "" : " WHERE " + req.Where.Trim();
        string orderBy = string.IsNullOrWhiteSpace(req.OrderBy) ? "" : " ORDER BY " + req.OrderBy.Trim();

        await using var connection = await OpenAsync(info: req.Connection ?? new ConnectionInfo(), ct);

        long total = 0;
        await using (var cmd = CreateCommand(connection))
        {
            cmd.CommandText = $"SELECT COUNT(*) FROM {qualified}{where}";
            total = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
        }

        string sql = $"SELECT * FROM {qualified}{where}{orderBy} LIMIT {offset}, {pageSize}";
        var result = await RunAsync(connection, sql, pageSize, ct);

        return new TableDataResult
        {
            IsQuery = true,
            Columns = result.Columns,
            ColumnTypes = result.ColumnTypes,
            Rows = result.Rows,
            RowCount = result.RowCount,
            ElapsedMs = result.ElapsedMs,
            Truncated = result.Truncated,
            Total = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    private async Task<QueryResult> RunAsync(MySqlConnection connection, string sql, int limit, CancellationToken ct)
    {
        var result = new QueryResult { IsQuery = IsQueryStatement(sql) };
        var sw = Stopwatch.StartNew();

        await using var cmd = CreateCommand(connection);
        cmd.CommandText = sql;
        cmd.CommandTimeout = _commandTimeout;

        if (!result.IsQuery)
        {
            int affected = await cmd.ExecuteNonQueryAsync(ct);
            sw.Stop();
            result.RowsAffected = affected;
            result.ElapsedMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
            result.Message = $"执行成功，影响行数 {affected}";
            return result;
        }

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        int fields = reader.FieldCount;
        for (int i = 0; i < fields; i++)
        {
            result.Columns.Add(reader.GetName(i));
            result.ColumnTypes.Add(new ColumnMeta
            {
                Name = reader.GetName(i),
                DataType = reader.GetDataTypeName(i),
            });
        }

        var rows = new List<object?[]>();
        while (await reader.ReadAsync(ct))
        {
            if (rows.Count >= limit)
            {
                result.Truncated = true;
                break;
            }

            var values = new object?[fields];
            for (int i = 0; i < fields; i++)
                values[i] = Normalize(reader.GetValue(i));
            rows.Add(values);
        }

        sw.Stop();
        result.Rows = rows;
        result.RowCount = rows.Count;
        result.ElapsedMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
        return result;
    }

    // ---------------------------------------------------------------- 导出

    public async Task<(byte[] Content, string FileName, int Rows)> ExportAsync(ConnectionInfo info, string sql, int? limit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentException("SQL 不能为空");

        int maxRows = ClampLimit(limit, _maxExportRows);
        await using var connection = await OpenAsync(info, ct);
        var result = await RunAsync(connection, sql, maxRows, ct);

        var sb = new StringBuilder();
        sb.Append(string.Join(",", result.Columns.Select(CsvEscape)));
        sb.Append('\n');

        foreach (var row in result.Rows)
        {
            sb.Append(string.Join(",", row.Select(v => CsvEscape(FormatValue(v)))));
            sb.Append('\n');
        }

        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(sb.ToString()))
            .ToArray();

        string fileName = $"export_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        return (bytes, fileName, result.Rows.Count);
    }

    private static string CsvEscape(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    private static string FormatValue(object? value) => value switch
    {
        null => "",
        bool b => b ? "1" : "0",
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"),
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    // ---------------------------------------------------------------- 工具

    private int ClampLimit(int? limit, int max) => Math.Clamp(limit ?? max, 1, Math.Max(max, 1));

    /// <summary>把驱动返回的特定类型转换成可 JSON 序列化的值。</summary>
    private static object? Normalize(object? value) => value switch
    {
        null or DBNull => null,
        byte[] bytes => "0x" + Convert.ToHexString(bytes),
        MySqlDateTime dt => dt.IsValidDateTime ? dt.GetDateTime() : dt.ToString(),
        MySqlGeometry geo => geo.ToString(),
        MySqlDecimal dec => dec.ToString(),
        _ => value,
    };

    /// <summary>判断语句是否返回结果集（而非影响行数）。</summary>
    internal static bool IsQueryStatement(string sql)
    {
        int i = 0;
        // 跳过前导空白与注释
        while (i < sql.Length)
        {
            if (char.IsWhiteSpace(sql[i])) { i++; continue; }

            if (i + 1 < sql.Length && sql[i] == '-' && sql[i + 1] == '-')
            {
                i += 2;
                while (i < sql.Length && sql[i] != '\n') i++;
                continue;
            }

            if (i + 1 < sql.Length && sql[i] == '/' && sql[i + 1] == '*')
            {
                i += 2;
                int end = sql.IndexOf("*/", i, StringComparison.Ordinal);
                i = end < 0 ? sql.Length : end + 2;
                continue;
            }

            break;
        }

        string head = sql[i..];
        int wordEnd = 0;
        while (wordEnd < head.Length && (char.IsLetterOrDigit(head[wordEnd]) || head[wordEnd] == '_'))
            wordEnd++;

        string word = head[..wordEnd].ToLowerInvariant();
        return word is "select" or "show" or "describe" or "desc" or "explain" or "with" or "values";
    }
}
