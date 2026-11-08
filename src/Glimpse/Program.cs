using Glimpse.Data;
using Glimpse.Helpers;
using Glimpse.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);
var metricsEnabled = builder.Configuration.GetValue<bool>("Metrics:Enabled");
var watchPath = builder.Configuration.GetWatchPath();
var dbPath = Path.GetFullPath(builder.Configuration.GetDatabasePath());

Directory.CreateDirectory(watchPath);
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

// Add MVC
builder.Services.AddControllersWithViews();

// Anti-forgery for CSRF protection. No CORS policy is registered on purpose: the UI is same-origin, and
// without one the browser already refuses cross-origin reads.
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
});

// Add Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Glimpse API", Version = "v1" });
});

// Database
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
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(watchPath),
    RequestPath = "/screenshots",
    OnPrepareResponse = ctx =>
    {
        // Screenshots are immutable - cache for 1 year
        ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
    }
});

app.UseRouting();
app.UseAntiforgery();

// Prometheus metrics (optional - enabled via Metrics:Enabled config)
if (metricsEnabled)
{
    app.UseWhen(
        ctx => !ctx.Request.Path.StartsWithSegments("/api/progress/stream"),
        appBuilder => appBuilder.UseHttpMetrics()
    );
    app.MapMetrics();
}

// SSE endpoint for progress and screenshot updates
app.MapGet("/api/progress/stream", (ScanProgressService progress, CancellationToken ct) =>
    TypedResults.ServerSentEvents(progress.GetUpdatesAsync(ct)));

// GPU stats endpoint
app.MapGet("/api/gpu", async (GpuStatsService gpu, CancellationToken ct) =>
    await gpu.GetStatsAsync(ct) is { } stats ? Results.Ok(stats) : Results.NoContent());

app.MapControllerRoute(
    name: "detail",
    pattern: "detail/{id}",
    defaults: new { controller = "Home", action = "Detail" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
