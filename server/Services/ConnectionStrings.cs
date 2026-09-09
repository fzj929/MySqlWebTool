using System.Data.Common;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using Dm;
using Oracle.ManagedDataAccess.Client;
using DataPilot.Api.Models;
using ConnectionInfo = DataPilot.Api.Models.ConnectionInfo;

namespace DataPilot.Api.Services;

public sealed partial class DatabaseService
{
    public string DefaultConnectionString(ConnectionInfo info)
    {
        info.Validate();
        // SQLite's normal factory opens a restricted connection; do not open it just to display parameters.
        if (info.DatabaseType == "sqlite") return new SqliteConnectionStringBuilder {
            DataSource = files.FilePath(info.FileId!), Mode = info.ReadOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite,
            Pooling = false, DefaultTimeout = 10
        }.ConnectionString;
        using var connection = CreateConnection(info, null);
        return connection.ConnectionString;
    }

    private DbConnectionStringBuilder ParseTestString(string type, string value) => type switch {
        "mysql" => new MySqlConnectionStringBuilder(value),
        "sqlserver" => new SqlConnectionStringBuilder(value),
        "postgresql" => new NpgsqlConnectionStringBuilder(value),
        "dm8" => new DmConnectionStringBuilder(value),
        "oracle" => new OracleConnectionStringBuilder(value),
        "sqlite" => new SqliteConnectionStringBuilder(value),
        _ => throw new ArgumentException("不支持的数据库类型")
    };

    public async Task<object> TestConnectionStringAsync(ConnectionStringRequest request, CancellationToken ct)
    {
        var info = request.Connection ?? throw new ArgumentException("缺少连接信息");
        if (string.IsNullOrWhiteSpace(request.ConnectionString) || request.ConnectionString.Length > 16384)
            throw new ArgumentException("连接字符串不能为空，且不能超过 16384 个字符");
        // Only allow fields produced by the existing provider factory. Native driver file/plugin/log
        // options must never turn this web endpoint into arbitrary server file access.
        var baseline = ParseTestString(info.DatabaseType, DefaultConnectionString(info));
        var supplied = ParseTestString(info.DatabaseType, request.ConnectionString);
        var allowed = new DbConnectionStringBuilder { ConnectionString = baseline.ConnectionString }.Keys.Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var suppliedKeys = new DbConnectionStringBuilder { ConnectionString = supplied.ConnectionString }.Keys.Cast<string>();
        if (suppliedKeys.Any(key => !allowed.Contains(key)))
            throw new ArgumentException("包含未开放的连接选项；请在默认字符串的字段范围内修改，不支持服务器文件、插件或日志路径选项。");
        if (supplied is SqliteConnectionStringBuilder sqlite)
        {
            var expected = (SqliteConnectionStringBuilder)baseline;
            if (sqlite.DataSource != expected.DataSource || sqlite.Mode != expected.Mode || sqlite.Pooling)
                throw new ArgumentException("SQLite 只能测试当前上传的数据库，请在顶部选择文件及只读模式；不能修改文件路径、访问模式或开启连接池。");
            return await TestAsync(info, ct);
        }
        // Safety boundaries and bounded probes are not overridable by pasted values.
        switch (supplied)
        {
            case MySqlConnectionStringBuilder b:
                if (b.AllowLoadLocalInfile) throw new ArgumentException("不能启用本地文件加载");
                b.ConnectionTimeout = 10; b.Pooling = false; break;
            case SqlConnectionStringBuilder b: b.ConnectTimeout = 10; b.Pooling = false; break;
            case NpgsqlConnectionStringBuilder b: b.Timeout = 10; b.CommandTimeout = 10; b.Pooling = false; break;
            case DmConnectionStringBuilder b: b.ConnectionTimeout = 10000; b.CommandTimeout = 10; b.ConnPooling = false; break;
            case OracleConnectionStringBuilder b:
                if (System.Text.RegularExpressions.Regex.IsMatch(b.DataSource, @"WALLET|IFILE|TNS_ADMIN|TRACE|TOKEN|CREDENTIAL", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    throw new ArgumentException("不支持文件或外部凭据相关连接选项");
                b.ConnectionTimeout = 10; b.Pooling = false; break;
        }
        await using DbConnection connection = info.DatabaseType switch {
            "mysql" => new MySqlConnection(supplied.ConnectionString),
            "sqlserver" => new SqlConnection(supplied.ConnectionString),
            "postgresql" => new NpgsqlConnection(supplied.ConnectionString),
            "dm8" => new DmConnection(supplied.ConnectionString),
            "oracle" => new OracleConnection(supplied.ConnectionString),
            _ => throw new ArgumentException("不支持的数据库类型")
        };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var watch = Stopwatch.StartNew();
        await connection.OpenAsync(deadline.Token);
        using var command = connection.CreateCommand();
        command.CommandTimeout = 10;
        command.CommandText = info.DatabaseType is "oracle" or "dm8" ? "SELECT 1 FROM DUAL" : "SELECT 1";
        await command.ExecuteScalarAsync(deadline.Token);
        return new { ok = true, databaseType = info.DatabaseType, serverVersion = connection.ServerVersion,
            elapsedMs = Math.Round(watch.Elapsed.TotalMilliseconds, 2) };
    }
}
