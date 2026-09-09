using DataPilot.Api.Endpoints;
using DataPilot.Api.Services;

var builder = WebApplication.CreateBuilder(args);
var certificateHosts = builder.Configuration["certificate-hosts"] ?? builder.Configuration["DataPilot:Https:Hosts"];

if (builder.Configuration.GetValue<bool>("create-certificate"))
{
    using var generated = HttpsCertificate.LoadOrCreate(HttpsCertificate.DirectoryPath(builder.Configuration), certificateHosts);
    Console.WriteLine($"Certificate subject: {generated.Subject}; expires: {generated.NotAfter:yyyy-MM-dd}");
    return;
}
var secure = !builder.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("DataPilot:Https:Enabled");
if (secure)
{
    var port = builder.Configuration.GetValue<int?>("https-port") ?? builder.Configuration.GetValue<int?>("DataPilot:Https:Port") ?? 8088;
    if (port is < 1 or > 65535) throw new ArgumentException("HTTPS port must be between 1 and 65535.");
    var cert = HttpsCertificate.LoadOrCreate(HttpsCertificate.DirectoryPath(builder.Configuration), certificateHosts);
    builder.WebHost.ConfigureKestrel(o => o.ListenAnyIP(port, endpoint => endpoint.UseHttpOrHttps(cert)));
    builder.Services.AddHttpsRedirection(o => { o.HttpsPort = port; o.RedirectStatusCode = StatusCodes.Status307TemporaryRedirect; });
}

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

if (app.Environment.IsDevelopment()) app.UseCors("dev");
if (secure)
{
    app.UseHttpsRedirection();
    app.Use(async (context, next) => {
        if (!context.Request.IsHttps) { context.Response.StatusCode = 400; return; }
        context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000";
        await next();
    });
}
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
