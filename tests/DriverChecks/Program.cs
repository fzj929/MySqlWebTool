using System.Reflection;
using Dm;
using DataPilot.Api.Models;
using DataPilot.Api.Services;
using Microsoft.Extensions.Configuration;

static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
var config = new ConfigurationBuilder().Build();
var service = new DatabaseService(config, null!, null!);
Check(DatabaseService.CreateDatabaseSql("mysql", "a`; DROP DATABASE x; --") == "CREATE DATABASE `a``; DROP DATABASE x; --`", "MySQL database name stays a single quoted identifier");
Check(DatabaseService.CreateDatabaseSql("sqlserver", "a]b") == "CREATE DATABASE [a]]b]", "SQL Server database identifier escaping");
Check(DatabaseService.CreateDatabaseSql("postgresql", "a\"b") == "CREATE DATABASE \"a\"\"b\"", "PostgreSQL database identifier escaping");
foreach (var (engine, name) in new[] { ("mysql", ""), ("mysql", " x"), ("mysql", "a\0b"),
    ("mysql", new string('x', 65)), ("sqlserver", new string('x', 129)),
    ("postgresql", new string('库', 22)), ("sqlite", "test"), ("dm8", "test"), ("oracle", "test") })
{
    try { DatabaseService.CreateDatabaseSql(engine, name); throw new Exception("Invalid database name or engine accepted"); }
    catch (ArgumentException) { }
}
Console.WriteLine("PASS: create database identifier escaping, length validation and supported engines");
var info = new ConnectionInfo { DatabaseType = "dm8", Host = "127.0.0.1", Port = 5236,
    User = "TEST_USER", Password = "synthetic;password=\"value\"", Schema = "MixedCase" };
info.Validate();
using var connection = (DmConnection)typeof(DatabaseService).GetMethod("CreateConnection", BindingFlags.NonPublic | BindingFlags.Instance)!
    .Invoke(service, new object?[] { info, "ignored-instance-label" })!;
var builder = new DmConnectionStringBuilder(connection.ConnectionString);
Check(builder.Password == info.Password, "Password must round-trip without connection-string injection");
Check(builder.Server == info.Host && builder.Port == 5236 && builder.User == info.User, "DM8 connection parameters");
Check(builder.Schema == "MixedCase" && builder.SchemaSensitive, "Preserve schema case");
Check(builder.ConnectionTimeout == 10000 && builder.CommandTimeout == 60, "DM8 timeout units");
Check(builder.ClobAsString && !builder.Varchar36ToGuid && !builder.ConnPooling, "DM8 value and session settings");
Check(DatabaseService.Qualified(info, "ignored", "A\"B", "order") == "\"A\"\"B\".\"order\"", "Quote schema safely");
info.Schema = "";
Check(DatabaseService.Qualified(info, "ignored", "", "table") == "\"table\"", "Default schema is server-resolved");
using var command = connection.CreateCommand();
command.CommandText = "SELECT ? FROM DUAL";
var parameter = command.CreateParameter(); parameter.ParameterName = "value"; parameter.Value = "synthetic";
command.Parameters.Add(parameter);
Check(command.Parameters.Count == 1, "ADO.NET command construction");
Console.WriteLine("PASS: actual net8 DM8 driver loads; connection builder, password escaping, schema case, quoting and command construction (no live server)");

var oracleInfo = new ConnectionInfo { DatabaseType = "oracle", Host = "127.0.0.1", Port = 1521,
    User = "TEST_USER", Password = "synthetic;password=\"value\"", Database = "FREEPDB1" };
foreach (var kind in new[] { "service", "sid" })
{
    oracleInfo.OracleConnectionType = kind;
    using var oc = (Oracle.ManagedDataAccess.Client.OracleConnection)typeof(DatabaseService).GetMethod("CreateConnection", BindingFlags.NonPublic | BindingFlags.Instance)!
        .Invoke(service, new object?[] { oracleInfo, "ignored" })!;
    var ob = new Oracle.ManagedDataAccess.Client.OracleConnectionStringBuilder(oc.ConnectionString);
    Check(ob.Password == oracleInfo.Password && !ob.Pooling, "Oracle password and session isolation");
    Check(ob.DataSource.Contains(kind == "sid" ? "(SID=FREEPDB1)" : "(SERVICE_NAME=FREEPDB1)"), "Oracle connection mode");
    using var cmd = (Oracle.ManagedDataAccess.Client.OracleCommand)typeof(DatabaseService).GetMethod("Command", BindingFlags.NonPublic | BindingFlags.Instance)!
        .Invoke(service, new object?[] { oc, "SELECT ? FROM DUAL WHERE ?=?", new (string, object?)[] { ("x", "a"), ("y", "b"), ("z", "b") } })!;
    Check(cmd.BindByName && cmd.CommandText == "SELECT :p0 FROM DUAL WHERE :p1=:p2" && cmd.Parameters.Count == 3, "Oracle named metadata parameters");
}
Check(DatabaseService.OracleSql("SELECT 1 FROM DUAL;  ") == "SELECT 1 FROM DUAL", "Strip SQL terminator");
Check(DatabaseService.OracleSql("-- comment\nBEGIN NULL; END;").EndsWith("END;"), "Preserve PL/SQL terminator");
Check(DatabaseService.OracleSql("CREATE OR REPLACE PROCEDURE p AS BEGIN NULL; END;").EndsWith("END;"), "Preserve stored procedure terminator");
oracleInfo.Database = "bad)(HOST=other)";
try { oracleInfo.Validate(); throw new Exception("Descriptor injection accepted"); } catch (ArgumentException) { }
Console.WriteLine("PASS: Oracle driver, service/SID, password escaping, named binding and SQL terminators (no live server)");
