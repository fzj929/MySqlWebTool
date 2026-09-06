using DataPilot.Api.Endpoints;
using DataPilot.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Use the Windows Service lifetime when launched by the Service Control Manager.
// Normal console launches continue to work unchanged.
builder.Host.UseWindowsService();

builder.Services.AddScoped<IMySqlService, MySqlService>();
builder.Services.AddScoped<MySqlService>();
builder.Services.AddScoped<DatabaseService>();
builder.Services.AddSingleton<SqliteStore>();
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
    o.MultipartBodyLengthLimit = (builder.Configuration.GetValue<long?>("DataPilot:MaxUploadBytes") ?? 104857600) + 1048576);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize =
    (builder.Configuration.GetValue<long?>("DataPilot:MaxUploadBytes") ?? 104857600) + 1048576);

// 本地开发工具：允许来自 Vite 开发服务器的跨域请求
builder.Services.AddCors(options =>
{
    options.AddPolicy("dev", policy => policy
        .AllowAnyOrigin()
        .AllowAnyHeader()
        .AllowAnyMethod());
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "DataPilot 数据库工作台 API",
        Version = "v1",
    });
});

var app = builder.Build();

app.UseCors("dev");
app.UseDefaultFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "DataPilot 数据库工作台 API v1"));
}

app.MapApiEndpoints();

// 若前端已构建到 wwwroot，则启用 SPA 回退
var webRoot = app.Environment.WebRootPath;
if (!string.IsNullOrEmpty(webRoot) && File.Exists(Path.Combine(webRoot, "index.html")))
{
    app.MapFallbackToFile("index.html");
}

app.Run();
