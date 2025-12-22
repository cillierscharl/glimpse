using Glimpse.Data;
using Glimpse.Services;
using Microsoft.EntityFrameworkCore;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

// Add MVC
builder.Services.AddControllersWithViews();

// Add Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Glimpse API", Version = "v1" });
});

// Database
var dbPath = builder.Configuration.GetValue<string>("Database:Path") 
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "glimpse", "glimpse.db");
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
builder.Services.AddDbContext<GlimpseDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// Services
builder.Services.AddSingleton<OcrService>();
builder.Services.AddSingleton<ScanProgressService>();
builder.Services.AddSingleton<GpuStatsService>();
builder.Services.AddSingleton<ScreenshotWatcherService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ScreenshotWatcherService>());

var app = builder.Build();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GlimpseDbContext>();
    db.Database.EnsureCreated();
}

app.UseStaticFiles();

// Swagger
app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Glimpse API v1"));

// Serve screenshot files
var watchPath = builder.Configuration.GetValue<string>("Screenshots:WatchPath")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures/Screenshots");
if (Directory.Exists(watchPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(watchPath),
        RequestPath = "/screenshots",
        OnPrepareResponse = ctx =>
        {
            // Screenshots are immutable - cache for 1 year
            ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        }
    });
}

app.UseRouting();

// Prometheus metrics (skip SSE endpoint)
app.UseWhen(
    ctx => !ctx.Request.Path.StartsWithSegments("/api/progress/stream"),
    appBuilder => appBuilder.UseHttpMetrics()
);
app.MapMetrics();

// SSE endpoint for progress and screenshot updates
app.MapGet("/api/progress/stream", (ScanProgressService progress, CancellationToken ct) =>
    TypedResults.ServerSentEvents(progress.GetUpdatesAsync(ct)));

// GPU stats endpoint
app.MapGet("/api/gpu", async (GpuStatsService gpu) =>
    await gpu.GetStatsAsync() is { } stats ? Results.Ok(stats) : Results.NoContent());

app.MapControllerRoute(
    name: "detail",
    pattern: "detail/{id}",
    defaults: new { controller = "Home", action = "Detail" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
