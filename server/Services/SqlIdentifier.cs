namespace MySqlTool.Api.Services;

/// <summary>
/// MySQL 标识符（库名/表名/列名）转义。
/// 注意：仅对长度与空字符做校验，表名等来自用户输入的树形浏览操作。
/// </summary>
public static class SqlIdentifier
{
    public static string Quote(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("标识符不能为空");
        if (identifier.Length > 64)
            throw new ArgumentException($"标识符过长（>64）：{identifier}");
        if (identifier.Contains('\0'))
            throw new ArgumentException("标识符包含非法字符");

        return "`" + identifier.Replace("`", "``") + "`";
    }

    /// <summary>库名.表名 组合转义</summary>
    public static string Qualified(string database, string table)
    {
        if (string.IsNullOrWhiteSpace(database)) return Quote(table);
        return Quote(database) + "." + Quote(table);
    }
}
