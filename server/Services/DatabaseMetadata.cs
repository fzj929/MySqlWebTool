using System.Data.Common;
using System.Text;
using DataPilot.Api.Models;
using ConnectionInfo = DataPilot.Api.Models.ConnectionInfo;

namespace DataPilot.Api.Services;

public sealed partial class DatabaseService
{
    public Task<TableSchemaResult> TableSchemaAsync(ConnectionInfo info, string database, string schema, string table, CancellationToken ct) =>
        WithConnection(info, database, c => SchemaOnConnection(info, c, database, schema, table, ct), ct);

    private async Task<TableSchemaResult> SchemaOnConnection(ConnectionInfo info, DbConnection c, string database, string schema, string table, CancellationToken ct)
    {
        if (info.DatabaseType == "mysql") return await mysql.GetTableSchemaAsync(info, database, table, ct);
        if (info.DatabaseType is "dm8" or "oracle") return await DmSchema(c, Schema(info, schema), table, ct);
        return info.DatabaseType == "sqlite" ? await SqliteSchema(c, table, ct) :
            info.DatabaseType == "postgresql" ? await PostgresSchema(c, Schema(info,schema), table, ct) :
            await SqlServerSchema(c, Schema(info,schema), table, ct);
    }

    private static void Aggregate(TableSchemaResult result, List<Dictionary<string, object?>> indexes)
    {
        foreach (var group in indexes.GroupBy(x => S(x,"name")))
        {
            var first = group.First();
            var columns = group.OrderBy(x => Convert.ToInt32(x["seq"])).Select(x => S(x,"column_name")).ToList();
            var primary = B(first, "is_primary");
            result.IndexDefinitions.Add(new IndexDefinition { Name = group.Key, Primary = primary, Unique = B(first,"is_unique"), Columns = columns });
            foreach (var row in group)
                result.Indexes.Add(new IndexInfo { Name = group.Key, Column = S(row,"column_name"), Unique = B(row,"is_unique"),
                    Seq = Convert.ToInt32(row["seq"]) });
            foreach (var col in result.Columns.Where(x => columns.Contains(x.Name)))
                if (primary) col.Key = "PRI"; else if (col.Key == "") col.Key = B(first,"is_unique") ? "UNI" : "MUL";
        }
    }

    private async Task<TableSchemaResult> SqliteSchema(DbConnection c, string table, CancellationToken ct)
    {
        var result = new TableSchemaResult();
        var objects = await Rows(c, "SELECT type,sql FROM sqlite_schema WHERE name=@name AND type IN ('table','view')", ct, ("@name",table));
        if (objects.Count == 0) throw new ArgumentException("表或视图不存在");
        foreach (var row in await Rows(c, "SELECT * FROM pragma_table_xinfo(@name)", ct, ("@name",table)))
            result.Columns.Add(new ColumnInfo { Ordinal = Convert.ToInt32(row["cid"]) + 1, Name = S(row,"name"),
                ColumnType = S(row,"type"), DataType = S(row,"type"), Nullable = !B(row,"notnull") && !B(row,"pk"),
                DefaultValue = row["dflt_value"]?.ToString(), Key = B(row,"pk") ? "PRI" : "", Extra = B(row,"hidden") ? "GENERATED / HIDDEN" : "" });
        var indexes = new List<Dictionary<string, object?>>();
        foreach (var idx in await Rows(c, "SELECT * FROM pragma_index_list(@name)", ct, ("@name",table)))
            foreach (var col in await Rows(c, "SELECT * FROM pragma_index_xinfo(@name) WHERE key=1", ct, ("@name",S(idx,"name"))))
                indexes.Add(new() { ["name"]=S(idx,"name"), ["column_name"]=S(col,"name") == "" ? "(expression)" : S(col,"name"),
                    ["seq"]=Convert.ToInt32(col["seqno"])+1, ["is_primary"]=S(idx,"origin")=="pk", ["is_unique"]=B(idx,"unique") });
        Aggregate(result,indexes);
        if (!result.IndexDefinitions.Any(x => x.Primary))
        {
            var keys = result.Columns.Where(x => x.Key=="PRI").Select(x => x.Name).ToList();
            if (keys.Count>0) result.IndexDefinitions.Insert(0,new IndexDefinition { Name="PRIMARY", Primary=true, Unique=true, Columns=keys });
        }
        var ddl = new StringBuilder(S(objects[0],"sql").TrimEnd(';')+";");
        foreach (var row in await Rows(c, "SELECT sql FROM sqlite_schema WHERE tbl_name=@name AND type IN ('index','trigger') AND sql IS NOT NULL ORDER BY type,name", ct, ("@name",table)))
            ddl.AppendLine().AppendLine(S(row,"sql").TrimEnd(';')+";");
        result.Ddl=ddl.ToString();
        return result;
    }

    private async Task<TableSchemaResult> PostgresSchema(DbConnection c, string schema, string table, CancellationToken ct)
    {
        var args = new[] { ("@schema",(object?)schema), ("@table",(object?)table) };
        var objects = await Rows(c, """
            SELECT c.oid,c.relkind,obj_description(c.oid) AS comment
            FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE n.nspname=@schema AND c.relname=@table
            """, ct,args);
        if (objects.Count==0) throw new ArgumentException("表或视图不存在");
        var result = new TableSchemaResult { DdlSource="generated" };
        var columns = await Rows(c, """
            SELECT a.attnum AS ordinal,a.attname AS name,format_type(a.atttypid,a.atttypmod) AS type,
                a.attnotnull AS notnull,pg_get_expr(d.adbin,d.adrelid) AS default_value,
                a.attidentity AS identity,a.attgenerated AS generated,col_description(a.attrelid,a.attnum) AS comment
            FROM pg_attribute a JOIN pg_class c ON c.oid=a.attrelid JOIN pg_namespace n ON n.oid=c.relnamespace
            LEFT JOIN pg_attrdef d ON d.adrelid=a.attrelid AND d.adnum=a.attnum
            WHERE n.nspname=@schema AND c.relname=@table AND a.attnum>0 AND NOT a.attisdropped ORDER BY a.attnum
            """,ct,args);
        foreach(var row in columns)
            result.Columns.Add(new ColumnInfo { Ordinal=Convert.ToInt32(row["ordinal"]),Name=S(row,"name"),ColumnType=S(row,"type"),
                DataType=S(row,"type"),Nullable=!B(row,"notnull"),DefaultValue=row["default_value"]?.ToString(),Comment=S(row,"comment"),
                Extra=S(row,"identity")!="" ? "IDENTITY" : S(row,"generated")!="" ? "GENERATED" : "" });
        var indexRows = await Rows(c, """
            SELECT ic.relname AS name, i.indisprimary AS is_primary,i.indisunique AS is_unique,
                k.ordinality AS seq,COALESCE(a.attname,pg_get_indexdef(i.indexrelid,k.ordinality::int,true)) AS column_name
            FROM pg_index i JOIN pg_class t ON t.oid=i.indrelid JOIN pg_namespace n ON n.oid=t.relnamespace
            JOIN pg_class ic ON ic.oid=i.indexrelid
            CROSS JOIN LATERAL unnest(i.indkey) WITH ORDINALITY k(attnum,ordinality)
            LEFT JOIN pg_attribute a ON a.attrelid=t.oid AND a.attnum=k.attnum
            WHERE n.nspname=@schema AND t.relname=@table ORDER BY ic.relname,k.ordinality
            """,ct,args);
        Aggregate(result,indexRows);
        var name=Quote("postgresql",schema)+"."+Quote("postgresql",table);
        var kind=S(objects[0],"relkind");
        var ddl=new StringBuilder();
        if(kind is "v" or "m")
        {
            var views=await Rows(c,"SELECT pg_get_viewdef(@oid::oid,true) AS definition",ct,("@oid",objects[0]["oid"]));
            ddl.Append($"CREATE {(kind=="m"?"MATERIALIZED ":"")}VIEW {name} AS\n{S(views[0],"definition")}");
        }
        else
        {
            var lines=new List<string>();
            foreach(var row in columns)
            {
                var line="  "+Quote("postgresql",S(row,"name"))+" "+S(row,"type");
                if(S(row,"identity")!="") line+=" GENERATED "+(S(row,"identity")=="a"?"ALWAYS":"BY DEFAULT")+" AS IDENTITY";
                else if(S(row,"generated")!="") line+=" GENERATED ALWAYS AS ("+S(row,"default_value")+") STORED";
                else if(S(row,"default_value")!="") line+=" DEFAULT "+S(row,"default_value");
                if(B(row,"notnull")) line+=" NOT NULL";
                lines.Add(line);
            }
            var constraints=await Rows(c, """
                SELECT con.conname AS name, pg_get_constraintdef(con.oid,true) AS definition
                FROM pg_constraint con JOIN pg_class t ON t.oid=con.conrelid JOIN pg_namespace n ON n.oid=t.relnamespace
                WHERE n.nspname=@schema AND t.relname=@table AND con.contype IN ('p','u','f','c','x') ORDER BY con.conname
                """,ct,args);
            lines.AddRange(constraints.Select(row=>"  CONSTRAINT "+Quote("postgresql",S(row,"name"))+" "+S(row,"definition")));
            ddl.Append($"CREATE TABLE {name} (\n{string.Join(",\n",lines)}\n);");
            if(kind=="p") result.Warnings.Add("分区策略需通过 pg_dump 获取完整定义。");
        }
        var indexDdl=await Rows(c, """
            SELECT pg_get_indexdef(i.indexrelid) AS definition FROM pg_index i
            JOIN pg_class t ON t.oid=i.indrelid JOIN pg_namespace n ON n.oid=t.relnamespace
            WHERE n.nspname=@schema AND t.relname=@table
            AND NOT EXISTS (SELECT 1 FROM pg_constraint co WHERE co.conindid=i.indexrelid)
            """,ct,args);
        foreach(var row in indexDdl) ddl.Append("\n").Append(S(row,"definition")).Append(';');
        static string Lit(string s)=>"'"+s.Replace("'","''")+"'";
        if(S(objects[0],"comment")!="") ddl.Append($"\nCOMMENT ON {(kind=="v"?"VIEW":kind=="m"?"MATERIALIZED VIEW":"TABLE")} {name} IS {Lit(S(objects[0],"comment"))};");
        foreach(var row in columns.Where(r=>S(r,"comment")!=""))
            ddl.Append($"\nCOMMENT ON COLUMN {name}.{Quote("postgresql",S(row,"name"))} IS {Lit(S(row,"comment"))};");
        result.Warnings.Add("根据系统目录重建 DDL；序列、触发器、权限、分区和扩展依赖请用 pg_dump 完整备份。");
        result.Ddl=ddl.ToString();
        return result;
    }

    private async Task<TableSchemaResult> SqlServerSchema(DbConnection c, string schema, string table, CancellationToken ct)
    {
        var name=Quote("sqlserver",schema)+"."+Quote("sqlserver",table);
        var args=new[] { ("@name",(object?)name) };
        var objects=await Rows(c,"SELECT type,OBJECT_DEFINITION(object_id) AS definition FROM sys.objects WHERE object_id=OBJECT_ID(@name) AND type IN ('U','V')",ct,args);
        if(objects.Count==0) throw new ArgumentException("表或视图不存在，或没有元数据权限");
        var result=new TableSchemaResult { DdlSource="generated" };
        var columns=await Rows(c, """
            SELECT c.column_id AS ordinal,c.name,c.is_nullable,c.is_identity,c.is_computed,
                CASE WHEN t.is_user_defined=1 THEN QUOTENAME(SCHEMA_NAME(t.schema_id))+'.'+QUOTENAME(t.name)
                ELSE t.name+CASE
                WHEN t.name IN ('varchar','char','varbinary','binary') THEN '('+CASE WHEN c.max_length=-1 THEN 'max' ELSE CAST(c.max_length AS varchar) END+')'
                WHEN t.name IN ('nvarchar','nchar') THEN '('+CASE WHEN c.max_length=-1 THEN 'max' ELSE CAST(c.max_length/2 AS varchar) END+')'
                WHEN t.name IN ('decimal','numeric') THEN '('+CAST(c.precision AS varchar)+','+CAST(c.scale AS varchar)+')'
                WHEN t.name IN ('datetime2','datetimeoffset','time') THEN '('+CAST(c.scale AS varchar)+')'
                ELSE '' END END AS type,
                dc.definition AS default_value,cc.definition AS expression,cc.is_persisted,
                ic.seed_value,ic.increment_value,CAST(ep.value AS nvarchar(4000)) AS comment
            FROM sys.columns c JOIN sys.types t ON t.user_type_id=c.user_type_id
            LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id
            LEFT JOIN sys.computed_columns cc ON cc.object_id=c.object_id AND cc.column_id=c.column_id
            LEFT JOIN sys.identity_columns ic ON ic.object_id=c.object_id AND ic.column_id=c.column_id
            LEFT JOIN sys.extended_properties ep ON ep.major_id=c.object_id AND ep.minor_id=c.column_id AND ep.name='MS_Description' AND ep.class=1
            WHERE c.object_id=OBJECT_ID(@name) ORDER BY c.column_id
            """,ct,args);
        foreach(var row in columns)
            result.Columns.Add(new ColumnInfo { Ordinal=Convert.ToInt32(row["ordinal"]),Name=S(row,"name"),ColumnType=S(row,"type"),
                DataType=S(row,"type"),Nullable=B(row,"is_nullable"),DefaultValue=row["default_value"]?.ToString(),Comment=S(row,"comment"),
                Extra=B(row,"is_identity")?"IDENTITY":B(row,"is_computed")?"COMPUTED":"" });
        var indexes=await Rows(c, """
            SELECT i.name,i.is_primary_key AS is_primary,i.is_unique,i.is_unique_constraint,
                i.type_desc,i.filter_definition,ic.key_ordinal AS seq,c.name AS column_name,ic.is_descending_key,ic.is_included_column
            FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
            JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
            WHERE i.object_id=OBJECT_ID(@name) AND i.index_id>0 AND i.is_hypothetical=0
            ORDER BY i.name,ic.key_ordinal,ic.index_column_id
            """,ct,args);
        Aggregate(result,indexes.Where(x=>!B(x,"is_included_column")).ToList());
        if(S(objects[0],"type").Trim()=="V")
        {
            result.Ddl=S(objects[0],"definition");
            result.DdlSource="native";
            if(result.Ddl=="") result.Warnings.Add("没有 VIEW DEFINITION 权限或视图已加密。");
            return result;
        }
        var lines=new List<string>();
        foreach(var row in columns)
        {
            var line="  "+Quote("sqlserver",S(row,"name"));
            if(B(row,"is_computed")) line+=" AS "+S(row,"expression")+(B(row,"is_persisted")?" PERSISTED":"");
            else
            {
                line+=" "+S(row,"type");
                if(B(row,"is_identity")) line+=$" IDENTITY({S(row,"seed_value")},{S(row,"increment_value")})";
                line+=B(row,"is_nullable")?" NULL":" NOT NULL";
                if(S(row,"default_value")!="") line+=" DEFAULT "+S(row,"default_value");
            }
            lines.Add(line);
        }
        var trailing=new List<string>();
        foreach(var group in indexes.GroupBy(x=>S(x,"name")))
        {
            var row=group.First();
            if(S(row,"type_desc") is not ("CLUSTERED" or "NONCLUSTERED"))
            { result.Warnings.Add($"索引 {group.Key} 为 {S(row,"type_desc")}，请用 SQL Server 工具完整脚本化。"); continue; }
            var keys=string.Join(", ",group.Where(x=>!B(x,"is_included_column")).Select(x=>Quote("sqlserver",S(x,"column_name"))+(B(x,"is_descending_key")?" DESC":" ASC")));
            if(B(row,"is_primary") || B(row,"is_unique_constraint"))
                lines.Add("  CONSTRAINT "+Quote("sqlserver",group.Key)+(B(row,"is_primary")?" PRIMARY KEY ":" UNIQUE ")+S(row,"type_desc")+" ("+keys+")");
            else
            {
                var index="CREATE "+(B(row,"is_unique")?"UNIQUE ":"")+S(row,"type_desc")+" INDEX "+Quote("sqlserver",group.Key)+" ON "+name+" ("+keys+")";
                var included=group.Where(x=>B(x,"is_included_column")).Select(x=>Quote("sqlserver",S(x,"column_name"))).ToList();
                if(included.Count>0) index+=" INCLUDE ("+string.Join(", ",included)+")";
                if(S(row,"filter_definition")!="") index+=" WHERE "+S(row,"filter_definition");
                trailing.Add(index+";");
            }
        }
        foreach(var row in await Rows(c,"SELECT name,definition FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(@name)",ct,args))
            lines.Add("  CONSTRAINT "+Quote("sqlserver",S(row,"name"))+" CHECK "+S(row,"definition"));
        var foreign=await Rows(c, """
            SELECT f.name,pc.name AS parent_column,rs.name AS ref_schema,rt.name AS ref_table,rc.name AS ref_column,
                f.delete_referential_action_desc AS on_delete,f.update_referential_action_desc AS on_update
            FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id
            JOIN sys.columns pc ON pc.object_id=fc.parent_object_id AND pc.column_id=fc.parent_column_id
            JOIN sys.tables rt ON rt.object_id=fc.referenced_object_id JOIN sys.schemas rs ON rs.schema_id=rt.schema_id
            JOIN sys.columns rc ON rc.object_id=fc.referenced_object_id AND rc.column_id=fc.referenced_column_id
            WHERE f.parent_object_id=OBJECT_ID(@name) ORDER BY f.name,fc.constraint_column_id
            """,ct,args);
        foreach(var group in foreign.GroupBy(x=>S(x,"name")))
        {
            var row=group.First();
            lines.Add("  CONSTRAINT "+Quote("sqlserver",group.Key)+" FOREIGN KEY ("+
                string.Join(", ",group.Select(x=>Quote("sqlserver",S(x,"parent_column"))))+") REFERENCES "+
                Quote("sqlserver",S(row,"ref_schema"))+"."+Quote("sqlserver",S(row,"ref_table"))+" ("+
                string.Join(", ",group.Select(x=>Quote("sqlserver",S(x,"ref_column"))))+") ON DELETE "+
                S(row,"on_delete").Replace('_',' ')+" ON UPDATE "+S(row,"on_update").Replace('_',' '));
        }
        result.Ddl=$"CREATE TABLE {name} (\n{string.Join(",\n",lines)}\n);\n"+string.Join("\n",trailing);
        result.Warnings.Add("根据系统目录重建 DDL；触发器、权限、时态/分区表、压缩和文件组等高级属性需用 SQL Server 工具完整备份。");
        return result;
    }
}
