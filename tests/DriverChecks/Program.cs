using System.Reflection;
using Dm;
using DataPilot.Api.Models;
using DataPilot.Api.Services;
using Microsoft.Extensions.Configuration;

static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
var config = new ConfigurationBuilder().Build();
var service = new DatabaseService(config, null!, null!);
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
