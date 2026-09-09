using Oracle.ManagedDataAccess.Client;
using System.Text.RegularExpressions;
using ConnectionInfo = DataPilot.Api.Models.ConnectionInfo;

namespace DataPilot.Api.Services;

public sealed partial class DatabaseService
{
    private static OracleConnection CreateOracleConnection(ConnectionInfo info, int timeout)
    {
        // Validate before building a TNS descriptor; usernames/passwords use the provider's escaping.
        info.Validate();
        var key = info.OracleConnectionType == "sid" ? "SID" : "SERVICE_NAME";
        var descriptor = $"(DESCRIPTION=(CONNECT_TIMEOUT={timeout})(TRANSPORT_CONNECT_TIMEOUT={timeout})(RETRY_COUNT=0)(ADDRESS=(PROTOCOL=TCP)(HOST={info.Host})(PORT={info.Port}))(CONNECT_DATA=({key}={info.Database})))";
        return new OracleConnection(new OracleConnectionStringBuilder {
            DataSource = descriptor, UserID = info.User, Password = info.Password,
            Pooling = false, ConnectionTimeout = timeout
        }.ConnectionString);
    }

    public static string OracleSql(string sql)
    {
        // One SQL statement or PL/SQL block per execution. Keep block terminators intact.
        var leading = Regex.Replace(sql, @"\A(?:\s|--[^\r\n]*(?:\r?\n|$)|/\*[\s\S]*?\*/)*", "");
        var block = Regex.IsMatch(leading, @"\A(?:BEGIN\b|DECLARE\b|CREATE\s+(?:OR\s+REPLACE\s+)?(?:NONEDITIONABLE\s+|EDITIONABLE\s+)?(?:PROCEDURE|FUNCTION|PACKAGE|TRIGGER|TYPE)\b)", RegexOptions.IgnoreCase);
        return block ? sql.TrimEnd() : sql.TrimEnd().TrimEnd(';');
    }
}
