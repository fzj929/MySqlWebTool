using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using Dm;
using DataPilot.Api.Models;
using ConnectionInfo = DataPilot.Api.Models.ConnectionInfo;

namespace DataPilot.Api.Services;

/// <summary>Common execution pipeline; each engine supplies its connection, dialect and metadata.</summary>
public sealed partial class DatabaseService(IConfiguration config, MySqlService mysql, SqliteStore files)
{
    private int Timeout => Math.Clamp(config.GetValue<int?>("MySqlTool:CommandTimeoutSeconds") ?? 60, 1, 600);
    private int MaxRows => Math.Max(1, config.GetValue<int?>("MySqlTool:MaxRows") ?? 1000);
    private int MaxExport => Math.Max(1, config.GetValue<int?>("MySqlTool:MaxExportRows") ?? 50000);

    public static string Quote(string type, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\0') || name.Length > 512)
            throw new ArgumentException("无效的数据库对象名称");
        return type switch {
            "mysql" => "`" + name.Replace("`", "``") + "`",
            "sqlserver" => "[" + name.Replace("]", "]]") + "]",
            _ => "\"" + name.Replace("\"", "\"\"") + "\""
        };
    }

    public static string Schema(ConnectionInfo info, string value) => !string.IsNullOrEmpty(value) ? value :
        !string.IsNullOrEmpty(info.Schema) ? info.Schema : info.DatabaseType == "dm8" ? "" : info.DatabaseType == "sqlserver" ? "dbo" : "public";

    public static string Qualified(ConnectionInfo info, string database, string schema, string table) => info.DatabaseType switch {
        "mysql" => Quote("mysql", database) + "." + Quote("mysql", table),
        "sqlite" => Quote("sqlite", table),
        "dm8" when string.IsNullOrEmpty(Schema(info, schema)) => Quote("dm8", table),
        _ => Quote(info.DatabaseType, Schema(info, schema)) + "." + Quote(info.DatabaseType, table)
    };

    private async Task<T> WithConnection<T>(ConnectionInfo info, string? database, Func<DbConnection, Task<T>> action, CancellationToken ct)
    {
        info.Validate();
        var sqlite = info.DatabaseType == "sqlite";
        if (sqlite) await files.Gate.WaitAsync(ct);
        try
        {
            await using var c = CreateConnection(info, database);
            if (c.State != System.Data.ConnectionState.Open) await c.OpenAsync(ct);
            if (c is SqliteConnection sc)
            {
                var until = DateTime.UtcNow.AddSeconds(Timeout);
                SQLitePCL.raw.sqlite3_progress_handler(sc.Handle, 10000,
                    _ => ct.IsCancellationRequested || DateTime.UtcNow > until ? 1 : 0, null);
            }
            return await action(c);
        }
        finally { if (sqlite) files.Gate.Release(); }
    }

    private DbConnection CreateConnection(ConnectionInfo info, string? database)
    {
        var db = string.IsNullOrWhiteSpace(database) ? info.Database : database;
        var connectTimeout = Math.Clamp(config.GetValue<int?>("MySqlTool:ConnectTimeoutSeconds") ?? 10, 1, 120);
        return info.DatabaseType switch {
            "mysql" => new MySqlConnection(new MySqlConnectionStringBuilder {
                Server = info.Host, Port = (uint)info.Port, UserID = info.User, Password = info.Password,
                Database = db ?? "", SslMode = info.UseSsl ? MySqlSslMode.Required : MySqlSslMode.Preferred,
                AllowUserVariables = true, AllowLoadLocalInfile = false, ConnectionTimeout = (uint)connectTimeout
            }.ToString()),
            "sqlserver" => new SqlConnection(new SqlConnectionStringBuilder {
                DataSource = $"tcp:{info.Host},{info.Port}", UserID = info.User, Password = info.Password,
                InitialCatalog = string.IsNullOrWhiteSpace(db) ? "master" : db,
                Encrypt = info.Encrypt ? SqlConnectionEncryptOption.Mandatory : SqlConnectionEncryptOption.Optional,
                TrustServerCertificate = info.TrustServerCertificate, ConnectTimeout = connectTimeout,
                ApplicationName = "DataPilot"
            }.ToString()),
            "postgresql" => new NpgsqlConnection(new NpgsqlConnectionStringBuilder {
                Host = info.Host, Port = info.Port, Username = info.User, Password = info.Password,
                Database = string.IsNullOrWhiteSpace(db) ? "postgres" : db,
                SslMode = Enum.TryParse<Npgsql.SslMode>(info.SslMode, true, out var mode) ? mode : throw new ArgumentException("无效的 SSL Mode"),
                Timeout = connectTimeout, CommandTimeout = Timeout, ApplicationName = "DataPilot",
                SearchPath = string.IsNullOrWhiteSpace(info.Schema) ? "public" : Quote("postgresql", info.Schema)
            }.ToString()),
            "sqlite" => SqliteStore.Open(files.FilePath(info.FileId!), info.ReadOnly),
            "dm8" => CreateDmConnection(info, connectTimeout),
            _ => throw new ArgumentException("不支持的数据库")
        };
    }

    private DbCommand Command(DbConnection c, string sql, params (string Name, object? Value)[] parameters)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = Timeout;
        foreach (var (name, value) in parameters)
        {
            var p = cmd.CreateParameter(); p.ParameterName = name; p.Value = value ?? DBNull.Value; cmd.Parameters.Add(p);
        }
        return cmd;
    }

    private async Task<List<Dictionary<string, object?>>> Rows(DbConnection c, string sql, CancellationToken ct,
        params (string Name, object? Value)[] parameters)
    {
        using var cmd = Command(c, sql, parameters);
        using var r = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<Dictionary<string, object?>>();
        while (await r.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }
    private static string S(Dictionary<string, object?> row, string key) => row.TryGetValue(key, out var v) ? Convert.ToString(v, CultureInfo.InvariantCulture) ?? "" : "";
    private static bool B(Dictionary<string, object?> row, string key) => row.TryGetValue(key, out var v) && v != null && Convert.ToInt32(v) != 0;

    public Task<object> TestAsync(ConnectionInfo info, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        return WithConnection<object>(info, null, c => Task.FromResult<object>(new {
            ok = true, databaseType = info.DatabaseType, serverVersion = c.ServerVersion,
            currentDatabase = info.DatabaseType == "sqlite" ? "main" : c.Database, elapsedMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2)
        }), ct);
    }

    public async Task<List<DatabaseItem>> GetDatabasesAsync(ConnectionInfo info, CancellationToken ct)
    {
        if (info.DatabaseType == "mysql") return await mysql.GetDatabasesAsync(info, ct);
        return await WithConnection(info, null, async c => {
            if (info.DatabaseType == "sqlite") return new List<DatabaseItem> { new() { Name = "main", Charset = "UTF-8" } };
            // DM8 endpoints identify an instance, not a list of switchable MySQL-style databases.
            if (info.DatabaseType == "dm8") return new List<DatabaseItem> { new() { Name = string.IsNullOrWhiteSpace(c.Database) ? "DM8" : c.Database } };
            var sql = info.DatabaseType == "sqlserver"
                ? "SELECT name FROM sys.databases WHERE state=0 AND HAS_DBACCESS(name)=1 ORDER BY name"
                : "SELECT datname AS name FROM pg_database WHERE datallowconn AND NOT datistemplate AND has_database_privilege(datname,'CONNECT') ORDER BY datname";
            return (await Rows(c, sql, ct)).Select(r => new DatabaseItem { Name = S(r, "name") }).ToList();
        }, ct);
    }

    public Task<List<DatabaseItem>> SchemasAsync(ConnectionInfo info, string database, CancellationToken ct) =>
        WithConnection(info, database, async c => {
            if (info.DatabaseType is "mysql" or "sqlite") return new List<DatabaseItem>();
            if (info.DatabaseType == "dm8") return (await Rows(c, """
                SELECT DISTINCT OWNER AS name FROM ALL_OBJECTS WHERE OBJECT_TYPE IN ('TABLE','VIEW')
                UNION SELECT SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) AS name FROM DUAL ORDER BY name
                """, ct)).Select(r => new DatabaseItem { Name = S(r, "name") }).ToList();
            var sql = info.DatabaseType == "postgresql"
                ? "SELECT nspname AS name FROM pg_namespace WHERE nspname NOT LIKE 'pg_%' AND nspname <> 'information_schema' AND has_schema_privilege(oid,'USAGE') ORDER BY nspname"
                : "SELECT name FROM sys.schemas WHERE name NOT IN ('sys','INFORMATION_SCHEMA') AND schema_id < 16384 ORDER BY name";
            return (await Rows(c, sql, ct)).Select(r => new DatabaseItem { Name = S(r,"name") }).ToList();
        }, ct);

    public async Task<List<TableItem>> TablesAsync(ConnectionInfo info, string database, string schema, CancellationToken ct)
    {
        if (info.DatabaseType == "mysql") return await mysql.GetTablesAsync(info, database, ct);
        return await WithConnection(info, database, async c => {
            if (info.DatabaseType == "dm8") return await DmTables(c, Schema(info, schema), ct);
            var sql = info.DatabaseType switch {
                "sqlite" => "SELECT name, CASE type WHEN 'view' THEN 'VIEW' ELSE 'BASE TABLE' END AS type FROM sqlite_schema WHERE type IN ('table','view') AND name NOT LIKE 'sqlite_%' ORDER BY name",
                "postgresql" => "SELECT c.relname AS name, CASE WHEN c.relkind IN ('v','m') THEN 'VIEW' ELSE 'BASE TABLE' END AS type FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname=@schema AND c.relkind IN ('r','p','v','m') ORDER BY c.relname",
                _ => "SELECT TABLE_NAME AS name, TABLE_TYPE AS type FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA=@schema ORDER BY TABLE_NAME"
            };
            return (await Rows(c, sql, ct, ("@schema", Schema(info,schema)))).Select(r => new TableItem {
                Name = S(r,"name"), Type = S(r,"type"), Schema = info.DatabaseType == "sqlite" ? "" : Schema(info,schema)
            }).ToList();
        }, ct);
    }

    public Task<QueryResult> ExecuteAsync(ConnectionInfo info, string sql, int? limit, CancellationToken ct) =>
        WithConnection(info, null, c => Run(c, sql, Math.Clamp(limit ?? MaxRows, 1, MaxRows), ct), ct);

    private async Task<QueryResult> Run(DbConnection c, string sql, int limit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentException("SQL 不能为空");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(Timeout));
        ct = deadline.Token;
        var sw = Stopwatch.StartNew();
        using var cmd = Command(c, sql);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new QueryResult();
        // Execute the complete batch; display its first row-producing result.
        do
        {
            if (reader.FieldCount == 0) continue;
            if (result.IsQuery) { while (await reader.ReadAsync(ct)) { } continue; }
            result.IsQuery = true;
            for (var i = 0; i < reader.FieldCount; i++)
            {
                result.Columns.Add(reader.GetName(i));
                result.ColumnTypes.Add(new ColumnMeta { Name = reader.GetName(i), DataType = reader.GetDataTypeName(i) });
            }
            while (await reader.ReadAsync(ct))
            {
                if (result.Rows.Count >= limit) { result.Truncated = true; continue; }
                var values = new object?[reader.FieldCount];
                for (var i = 0; i < values.Length; i++)
                {
                    // DM DECIMAL(38) exceeds System.Decimal's 28-29 digits. Keep the provider's exact representation.
                    values[i] = reader is DmDataReader dm && !reader.IsDBNull(i) && reader.GetFieldType(i) == typeof(decimal)
                        ? dm.GetDmDecimal(i).ToString() : Normalize(reader.GetValue(i));
                }
                result.Rows.Add(values);
            }
        } while (await reader.NextResultAsync(ct));
        result.RowsAffected = Math.Max(0, reader.RecordsAffected);
        result.RowCount = result.Rows.Count;
        result.ElapsedMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
        result.Message = result.IsQuery ? "显示批次中的第一个结果集" : $"执行成功，影响行数 {result.RowsAffected}";
        return result;
    }

    private static object? Normalize(object? value) => value switch {
        null or DBNull => null,
        byte[] bytes => "0x" + Convert.ToHexString(bytes),
        long n => n.ToString(CultureInfo.InvariantCulture),
        ulong n => n.ToString(CultureInfo.InvariantCulture),
        decimal n => n.ToString(CultureInfo.InvariantCulture),
        double n when !double.IsFinite(n) => n.ToString(CultureInfo.InvariantCulture),
        float n when !float.IsFinite(n) => n.ToString(CultureInfo.InvariantCulture),
        DateTime d => d.ToString("O"),
        DateTimeOffset d => d.ToString("O"),
        DateOnly d => d.ToString("yyyy-MM-dd"),
        TimeOnly t => t.ToString("HH:mm:ss.fffffff"),
        MySqlDateTime d => d.ToString(),
        MySqlDecimal d => d.ToString(),
        System.Collections.IEnumerable items when value is not string => items.Cast<object?>().Select(Normalize).ToArray(),
        _ when value.GetType().IsPrimitive || value is string => value,
        _ => value.ToString()
    };

    public Task<TableDataResult> DataAsync(TableDataRequest req, CancellationToken ct)
    {
        var info = req.Connection ?? throw new ArgumentException("缺少连接信息");
        return WithConnection(info, req.Database, async c => {
            var name = Qualified(info, req.Database, req.Schema, req.Table);
            var w = string.IsNullOrWhiteSpace(req.Where) ? "" : " WHERE " + req.Where;
            var order = string.IsNullOrWhiteSpace(req.OrderBy) ? "" : " ORDER BY " + req.OrderBy;
            if (order == "")
            {
                var keys = info.DatabaseType == "dm8"
                    ? await DmPrimaryKeyColumns(c, await DmSchemaName(c, Schema(info, req.Schema), ct), req.Table, ct)
                    : (await SchemaOnConnection(info, c, req.Database, req.Schema, req.Table, ct)).IndexDefinitions.FirstOrDefault(x => x.Primary)?.Columns;
                if (keys?.Count > 0) order = " ORDER BY " + string.Join(", ", keys.Select(k => Quote(info.DatabaseType, k)));
                else if (info.DatabaseType == "sqlserver") order = " ORDER BY (SELECT NULL)";
            }
            var page = Math.Max(1,req.Page);
            var size = Math.Clamp(req.PageSize, 1, 1000);
            var offset = ((long)page - 1) * size;
            using var count = Command(c, $"SELECT COUNT(*) FROM {name}{w}");
            var total = Convert.ToInt64(await count.ExecuteScalarAsync(ct));
            var paging = info.DatabaseType == "sqlserver" ? $" OFFSET {offset} ROWS FETCH NEXT {size} ROWS ONLY" : $" LIMIT {size} OFFSET {offset}";
            var result = await Run(c, $"SELECT * FROM {name}{w}{order}{paging}", size, ct);
            return new TableDataResult { Columns = result.Columns, ColumnTypes = result.ColumnTypes, Rows = result.Rows,
                RowCount = result.RowCount, IsQuery = true, ElapsedMs = result.ElapsedMs, Total = total, Page = page, PageSize = size };
        }, ct);
    }

    public async Task<(byte[] Content, string FileName, int Rows)> ExportAsync(ConnectionInfo info, string sql, int? limit, CancellationToken ct)
    {
        var result = await WithConnection(info, null, c => Run(c, sql, Math.Clamp(limit ?? MaxExport,1,MaxExport), ct), ct);
        if (!result.IsQuery) throw new ArgumentException("CSV 导出需要返回结果集的 SQL");
        static string Escape(object? v)
        {
            var s = v is Array ? System.Text.Json.JsonSerializer.Serialize(v) : Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        var csv = new StringBuilder();
        csv.AppendLine(string.Join(",", result.Columns.Select(Escape)));
        foreach (var row in result.Rows) csv.AppendLine(string.Join(",",row.Select(Escape)));
        return (Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), $"DataPilot_{DateTime.Now:yyyyMMdd_HHmmss}.csv", result.RowCount);
    }
}
