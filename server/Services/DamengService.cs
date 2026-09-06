using System.Data.Common;
using System.Globalization;
using Dm;
using DataPilot.Api.Models;
using ConnectionInfo = DataPilot.Api.Models.ConnectionInfo;
using ColumnInfo = DataPilot.Api.Models.ColumnInfo;

namespace DataPilot.Api.Services;

public sealed partial class DatabaseService
{
    private DmConnection CreateDmConnection(ConnectionInfo info, int connectTimeout)
    {
        var builder = new DmConnectionStringBuilder {
            Server = info.Host, Port = info.Port, User = info.User, Password = info.Password,
            // DmProvider connection timeout is milliseconds; command timeout is seconds.
            ConnectionTimeout = connectTimeout * 1000, CommandTimeout = Timeout,
            AppName = "DataPilot", ClobAsString = true, Varchar36ToGuid = false,
            // Requests use independent sessions; SET SCHEMA/transactions must not leak between users.
            ConnPooling = false, SchemaSensitive = true
        };
        if (!string.IsNullOrWhiteSpace(info.Schema)) builder.Schema = info.Schema;
        return new DmConnection(builder.ConnectionString);
    }

    private async Task<string> DmSchemaName(DbConnection c, string schema, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(schema)) return schema;
        var rows = await Rows(c, "SELECT SF_GET_SCHEMA_NAME_BY_ID(CURRENT_SCHID()) AS name FROM DUAL", ct);
        return S(rows[0], "name");
    }

    private async Task<List<TableItem>> DmTables(DbConnection c, string schema, CancellationToken ct)
    {
        schema = await DmSchemaName(c, schema, ct);
        var rows = await Rows(c, """
            SELECT OBJECT_NAME AS name, OBJECT_TYPE AS type FROM ALL_OBJECTS
            WHERE OWNER=? AND OBJECT_TYPE IN ('TABLE','VIEW') ORDER BY OBJECT_NAME
            """, ct, ("schema", schema));
        return rows.Select(r => new TableItem {
            Name = S(r, "name"), Type = S(r, "type") == "VIEW" ? "VIEW" : "BASE TABLE", Schema = schema
        }).ToList();
    }

    private async Task<List<string>> DmPrimaryKeyColumns(DbConnection c, string schema, string table, CancellationToken ct)
    {
        var rows = await Rows(c, """
            SELECT cc.COLUMN_NAME AS name FROM ALL_CONSTRAINTS co
            JOIN ALL_CONS_COLUMNS cc ON cc.OWNER=co.OWNER AND cc.CONSTRAINT_NAME=co.CONSTRAINT_NAME AND cc.TABLE_NAME=co.TABLE_NAME
            WHERE co.OWNER=? AND co.TABLE_NAME=? AND co.CONSTRAINT_TYPE='P' ORDER BY cc.POSITION
            """, ct, ("schema", schema), ("table", table));
        return rows.Select(r => S(r, "name")).ToList();
    }

    private async Task<TableSchemaResult> DmSchema(DbConnection c, string schema, string table, CancellationToken ct)
    {
        schema = await DmSchemaName(c, schema, ct);
        // DmProvider binds positional ? parameters in SQL occurrence order.
        var args = new[] { ("schema", (object?)schema), ("table", (object?)table) };
        var objects = await Rows(c, """
            SELECT OBJECT_TYPE AS type FROM ALL_OBJECTS
            WHERE OWNER=? AND OBJECT_NAME=? AND OBJECT_TYPE IN ('TABLE','VIEW')
            """, ct, args);
        if (objects.Count == 0) throw new ArgumentException("表或视图不存在，或没有元数据权限");
        var result = new TableSchemaResult();
        var columns = await Rows(c, """
            SELECT c.COLUMN_ID AS ordinal, c.COLUMN_NAME AS name, c.DATA_TYPE AS type,
                c.DATA_LENGTH AS data_length, c.DATA_PRECISION AS data_precision, c.DATA_SCALE AS data_scale,
                c.NULLABLE AS nullable, c.DATA_DEFAULT AS default_value, cm.COMMENTS AS column_comment
            FROM ALL_TAB_COLUMNS c LEFT JOIN ALL_COL_COMMENTS cm
                ON cm.OWNER=c.OWNER AND cm.TABLE_NAME=c.TABLE_NAME AND cm.COLUMN_NAME=c.COLUMN_NAME
            WHERE c.OWNER=? AND c.TABLE_NAME=? ORDER BY c.COLUMN_ID
            """, ct, args);
        foreach (var row in columns)
        {
            var type = S(row, "type");
            var display = type;
            if (type.ToUpperInvariant() is "CHAR" or "VARCHAR" or "VARCHAR2" or "BINARY" or "VARBINARY")
                display += "(" + S(row, "data_length") + ")";
            else if (type.ToUpperInvariant() is "DECIMAL" or "DEC" or "NUMERIC" or "NUMBER" && S(row, "data_precision") != "")
                display += "(" + S(row, "data_precision") + "," + (S(row, "data_scale") is "" ? "0" : S(row, "data_scale")) + ")";
            result.Columns.Add(new ColumnInfo {
                Ordinal = Convert.ToInt32(row["ordinal"], CultureInfo.InvariantCulture), Name = S(row, "name"),
                DataType = type, ColumnType = display, Nullable = S(row, "nullable") == "Y",
                DefaultValue = row["default_value"]?.ToString(), Comment = S(row, "column_comment")
            });
        }
        var indexes = await Rows(c, """
            SELECT i.INDEX_NAME AS name, ic.COLUMN_NAME AS column_name, ic.COLUMN_POSITION AS seq,
                CASE WHEN i.UNIQUENESS='UNIQUE' THEN 1 ELSE 0 END AS is_unique,
                CASE WHEN EXISTS (SELECT 1 FROM ALL_CONSTRAINTS co
                    WHERE co.OWNER=i.TABLE_OWNER AND co.TABLE_NAME=i.TABLE_NAME
                    AND co.INDEX_NAME=i.INDEX_NAME AND co.CONSTRAINT_TYPE='P') THEN 1 ELSE 0 END AS is_primary
            FROM ALL_INDEXES i JOIN ALL_IND_COLUMNS ic ON ic.INDEX_OWNER=i.OWNER AND ic.INDEX_NAME=i.INDEX_NAME
                AND ic.TABLE_OWNER=i.TABLE_OWNER AND ic.TABLE_NAME=i.TABLE_NAME
            WHERE i.TABLE_OWNER=? AND i.TABLE_NAME=? ORDER BY i.INDEX_NAME, ic.COLUMN_POSITION
            """, ct, args);
        Aggregate(result, indexes);
        // Some clustered primary indexes have no entries in ALL_IND_COLUMNS.
        var keys = await DmPrimaryKeyColumns(c, schema, table, ct);
        if (keys.Count > 0)
        {
            foreach (var col in result.Columns.Where(x => keys.Contains(x.Name))) col.Key = "PRI";
            if (!result.IndexDefinitions.Any(x => x.Primary))
                result.IndexDefinitions.Insert(0, new IndexDefinition { Name = "PRIMARY", Primary = true, Unique = true, Columns = keys });
        }
        try
        {
            var ddl = await Rows(c, "SELECT DBMS_METADATA.GET_DDL(?, ?, ?) AS definition FROM DUAL", ct,
                ("kind", S(objects[0], "type")), ("name", table), ("schema", schema));
            result.Ddl = S(ddl[0], "definition");
            if (string.IsNullOrWhiteSpace(result.Ddl)) result.Warnings.Add("服务器未返回对象定义，请检查 GET_DDL 权限。");
        }
        catch (DbException) when (!ct.IsCancellationRequested)
        {
            result.DdlSource = "unavailable";
            result.Warnings.Add("无法读取 DBMS_METADATA.GET_DDL：请检查对象定义权限及服务器包支持。字段与索引仍可查看。");
        }
        result.Warnings.Add("DM8 原生对象 DDL 不代表完整备份；独立索引、触发器、权限、依赖对象和数据请使用达梦原生备份工具迁移。");
        return result;
    }
}
