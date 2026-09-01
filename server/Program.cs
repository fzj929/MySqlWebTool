using MySqlTool.Api.Endpoints;
using MySqlTool.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<IMySqlService, MySqlService>();

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
        Title = "MySQL Web 工具 API",
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
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "MySQL Web 工具 API v1"));
}

app.MapApiEndpoints();

// 若前端已构建到 wwwroot，则启用 SPA 回退
var webRoot = app.Environment.WebRootPath;
if (!string.IsNullOrEmpty(webRoot) && File.Exists(Path.Combine(webRoot, "index.html")))
{
    app.MapFallbackToFile("index.html");
}

app.Run();
