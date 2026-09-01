using MySqlConnector;
using MySqlTool.Api.Models;
using MySqlTool.Api.Services;
using ConnectionInfo = MySqlTool.Api.Models.ConnectionInfo;

namespace MySqlTool.Api.Endpoints;

public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/health", () => Results.Ok(new
        {
            status = "ok",
            service = "MySqlTool.Api",
            time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        }));

        api.MapPost("/connection/test", async (ConnectionInfo? info, IMySqlService svc, CancellationToken ct) =>
            await Safe(async () =>
            {
                var result = await svc.TestAsync(Require(info), ct);
                return Results.Ok(result);
            }));

        api.MapPost("/schema/databases", async (ConnectionInfo? info, IMySqlService svc, CancellationToken ct) =>
            await Safe(async () =>
            {
                var items = await svc.GetDatabasesAsync(Require(info), ct);
                return Results.Ok(new { items });
            }));

        api.MapPost("/schema/tables", async (SchemaRequest req, IMySqlService svc, CancellationToken ct) =>
            await Safe(async () =>
            {
                var items = await svc.GetTablesAsync(Require(req?.Connection), req!.Database, ct);
                return Results.Ok(new { items });
            }));

        api.MapPost("/schema/table", async (TableSchemaRequest req, IMySqlService svc, CancellationToken ct) =>
            await Safe(async () =>
            {
                var schema = await svc.GetTableSchemaAsync(Require(req?.Connection), req!.Database, req.Table, ct);
                return Results.Ok(schema);
            }));

        api.MapPost("/query", async (QueryRequest req, IMySqlService svc, CancellationToken ct) =>
            await Safe(async () =>
            {
                var result = await svc.ExecuteAsync(Require(req?.Connection), req!.Sql, req.Limit, ct);
                return Results.Ok(result);
            }));

        api.MapPost("/query/data", async (TableDataRequest req, IMySqlService svc, CancellationToken ct) =>
            await Safe(async () =>
            {
                var result = await svc.GetTableDataAsync(req, ct);
                return Results.Ok(result);
            }));

        api.MapPost("/query/export", async (QueryRequest req, IMySqlService svc, CancellationToken ct) =>
            await Safe(async () =>
            {
                var (content, fileName, rows) = await svc.ExportAsync(Require(req?.Connection), req!.Sql, req.Limit, ct);
                return Results.File(content, "text/csv; charset=utf-8", fileName);
            }));

        return app;
    }

    private static ConnectionInfo Require(ConnectionInfo? info)
    {
        if (info is null)
            throw new ArgumentException("缺少连接信息");
        info.Validate();
        return info;
    }

    private static async Task<IResult> Safe(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new ApiError { Error = ex.Message });
        }
        catch (MySqlException ex)
        {
            return Results.BadRequest(new ApiError { Error = $"MySQL 错误 [{ex.Number}]：{ex.Message}" });
        }
        catch (TimeoutException ex)
        {
            return Results.Json(new ApiError { Error = "执行超时：" + ex.Message }, statusCode: 408);
        }
        catch (OperationCanceledException)
        {
            return Results.Json(new ApiError { Error = "请求已取消" }, statusCode: 499);
        }
        catch (Exception ex)
        {
            return Results.Json(new ApiError { Error = "服务端错误：" + ex.Message }, statusCode: 500);
        }
    }
}
