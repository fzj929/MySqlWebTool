using System.Data.Common;
using DataPilot.Api.Models;
using DataPilot.Api.Services;
using ConnectionInfo = DataPilot.Api.Models.ConnectionInfo;

namespace DataPilot.Api.Endpoints;

public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/health", () => Results.Ok(new
        {
            status = "ok",
            service = "DataPilot.Api",
            time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        }));

        api.MapPost("/connection/test", async (ConnectionInfo? info, DatabaseService svc, CancellationToken ct) =>
            await Safe(async () =>
            {
                var result = await svc.TestAsync(Require(info), ct);
                return Results.Ok(result);
            }));

        api.MapPost("/schema/databases", async (ConnectionInfo? info, DatabaseService svc, CancellationToken ct) =>
            await Safe(async () =>
            {
                var items = await svc.GetDatabasesAsync(Require(info), ct);
                return Results.Ok(new { items });
            }));

        api.MapPost("/schema/schemas", async (SchemaRequest req, DatabaseService svc, CancellationToken ct) =>
            await Safe(async () => Results.Ok(new { items = await svc.SchemasAsync(Require(req.Connection), req.Database, ct) })));

        api.MapPost("/schema/tables", async (SchemaRequest req, DatabaseService svc, CancellationToken ct) =>
            await Safe(async () =>
            {
                var items = await svc.TablesAsync(Require(req?.Connection), req!.Database, req.Schema, ct);
                return Results.Ok(new { items });
            }));

        api.MapPost("/schema/table", async (TableSchemaRequest req, DatabaseService svc, CancellationToken ct) =>
            await Safe(async () =>
            {
                var schema = await svc.TableSchemaAsync(Require(req?.Connection), req!.Database, req.Schema, req.Table, ct);
                return Results.Ok(schema);
            }));

        api.MapPost("/query", async (QueryRequest req, DatabaseService svc, CancellationToken ct) =>
            await Safe(async () =>
            {
                var result = await svc.ExecuteAsync(Require(req?.Connection), req!.Sql, req.Limit, ct);
                return Results.Ok(result);
            }));

        api.MapPost("/query/data", async (TableDataRequest req, DatabaseService svc, CancellationToken ct) =>
            await Safe(async () =>
            {
                var result = await svc.DataAsync(req, ct);
                return Results.Ok(result);
            }));

        api.MapPost("/query/export", async (QueryRequest req, DatabaseService svc, CancellationToken ct) =>
            await Safe(async () =>
            {
                var (content, fileName, rows) = await svc.ExportAsync(Require(req?.Connection), req!.Sql, req.Limit, ct);
                return Results.File(content, "text/csv; charset=utf-8", fileName);
            }));

        api.MapGet("/sqlite/files", async (SqliteStore store, CancellationToken ct) => await Safe(async () =>
        {
            await store.Gate.WaitAsync(ct);
            try { return Results.Ok(new { items = store.List(), maxUploadBytes = store.MaxBytes }); }
            finally { store.Gate.Release(); }
        }));
        api.MapPost("/sqlite/files", async (HttpRequest request, SqliteStore store, CancellationToken ct) => await Safe(async () =>
        {
            if (!request.HasFormContentType) throw new ArgumentException("请上传数据库文件");
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file") ?? throw new ArgumentException("缺少 file 文件字段");
            return Results.Ok(await store.Upload(file, ct));
        }));
        api.MapDelete("/sqlite/files/{id}", async (string id, SqliteStore store, CancellationToken ct) => await Safe(async () =>
        {
            await store.Delete(id, ct);
            return Results.Ok(new { ok = true });
        }));
        api.MapGet("/sqlite/files/{id}/download", async (string id, SqliteStore store, CancellationToken ct) => await Safe(async () =>
        {
            var (bytes, name) = await store.Download(id, ct);
            return Results.File(bytes, "application/vnd.sqlite3", name);
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
        catch (DbException ex)
        {
            return Results.BadRequest(new ApiError { Error = $"数据库错误：{ex.Message}" });
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
