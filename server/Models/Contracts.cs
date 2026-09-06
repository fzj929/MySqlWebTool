using System.Text.Json.Serialization;

namespace DataPilot.Api.Models;

/// <summary>
/// 连接信息。由前端在每次请求时携带，服务端不持久化密码。
/// </summary>
public sealed class ConnectionInfo
{
    public string DatabaseType { get; set; } = "mysql";
    public string Schema { get; set; } = "";
    public string? FileId { get; set; }
    public bool ReadOnly { get; set; }
    public bool Encrypt { get; set; } = true;
    public bool TrustServerCertificate { get; set; }
    public string SslMode { get; set; } = "Prefer";
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 3306;
    public string User { get; set; } = "root";
    public string Password { get; set; } = "";
    public string? Database { get; set; }
    public bool UseSsl { get; set; }

    public void Validate()
    {
        if (DatabaseType is not ("mysql" or "sqlserver" or "postgresql" or "sqlite" or "dm8"))
            throw new ArgumentException("不支持的数据库类型");
        if (DatabaseType == "sqlite")
        {
            if (!Guid.TryParseExact(FileId, "N", out _)) throw new ArgumentException("请选择已上传的 SQLite 数据库");
            return;
        }
        if (string.IsNullOrWhiteSpace(Host)) throw new ArgumentException("主机地址不能为空");
        if (Port is < 1 or > 65535) throw new ArgumentException("端口必须在 1-65535 之间");
        if (string.IsNullOrWhiteSpace(User)) throw new ArgumentException("用户名不能为空");
    }
}

public sealed class SchemaRequest
{
    public string Schema { get; set; } = "";
    public ConnectionInfo? Connection { get; set; }
    public string Database { get; set; } = "";
}

public sealed class TableSchemaRequest
{
    public string Schema { get; set; } = "";
    public ConnectionInfo? Connection { get; set; }
    public string Database { get; set; } = "";
    public string Table { get; set; } = "";
}

public sealed class QueryRequest
{
    public ConnectionInfo? Connection { get; set; }
    public string Sql { get; set; } = "";
    public int? Limit { get; set; }
    public bool ForExport { get; set; }
}

public sealed class TableDataRequest
{
    public string Schema { get; set; } = "";
    public ConnectionInfo? Connection { get; set; }
    public string Database { get; set; } = "";
    public string Table { get; set; } = "";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;

    /// <summary>可选 WHERE 条件（不含 WHERE 关键字），来自使用者输入。</summary>
    public string? Where { get; set; }

    /// <summary>可选 ORDER BY 子句（不含 ORDER BY 关键字），来自使用者输入。</summary>
    public string? OrderBy { get; set; }
}

public class QueryResult
{
    public bool IsQuery { get; set; }
    public int RowCount { get; set; }
    public int RowsAffected { get; set; }
    public bool Truncated { get; set; }
    public List<string> Columns { get; set; } = new();
    public List<ColumnMeta> ColumnTypes { get; set; } = new();
    public List<object?[]> Rows { get; set; } = new();
    public double ElapsedMs { get; set; }
    public string? Message { get; set; }
}

public sealed class ColumnMeta
{
    public string Name { get; set; } = "";
    public string DataType { get; set; } = "";
}

public sealed class DatabaseItem
{
    public string Name { get; set; } = "";
    public string Charset { get; set; } = "";
}

public sealed class TableItem
{
    public string Schema { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string? Engine { get; set; }
    public long? Rows { get; set; }
    public decimal? SizeMb { get; set; }
    public string Comment { get; set; } = "";
}

public sealed class ColumnInfo
{
    public string Name { get; set; } = "";
    public string ColumnType { get; set; } = "";
    public string DataType { get; set; } = "";
    public bool Nullable { get; set; }
    public string? DefaultValue { get; set; }
    public string Key { get; set; } = "";
    public string Extra { get; set; } = "";
    public string Comment { get; set; } = "";
    public int Ordinal { get; set; }
}

public sealed class IndexInfo
{
    public string Name { get; set; } = "";
    public string Column { get; set; } = "";
    public bool Unique { get; set; }
    public int Seq { get; set; }
    public string? Cardinality { get; set; }
}

/// <summary>聚合后的索引定义，用于生成 DDL（支持复合索引）。</summary>
public sealed class IndexDefinition
{
    public string Name { get; set; } = "";
    public bool Unique { get; set; }
    public bool Primary { get; set; }
    public List<string> Columns { get; set; } = new();
}

/// <summary>表级元信息，用于生成完整建表语句。</summary>
public sealed class TableMeta
{
    public string? Engine { get; set; }
    public string? Collation { get; set; }
    public string? Comment { get; set; }
    public long? AutoIncrement { get; set; }
    public string? RowFormat { get; set; }
}

public sealed class TableSchemaResult
{
    public string DdlSource { get; set; } = "native";
    public List<string> Warnings { get; set; } = new();
    public List<ColumnInfo> Columns { get; set; } = new();
    public List<IndexInfo> Indexes { get; set; } = new();

    /// <summary>聚合后的索引（复合索引合并为一条），供 DDL 生成使用。</summary>
    public List<IndexDefinition> IndexDefinitions { get; set; } = new();

    /// <summary>表级属性（引擎、字符集、注释、自增值）。</summary>
    public TableMeta? Meta { get; set; }

    /// <summary>SHOW CREATE TABLE 的原始建表语句。</summary>
    public string Ddl { get; set; } = "";
}

public sealed class PageResult<T>
{
    public List<T> Items { get; set; } = new();
    public long Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public sealed class ApiError
{
    [JsonPropertyName("error")]
    public string Error { get; set; } = "";
}
