using Glimpse.Data;
using Glimpse.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add MVC
builder.Services.AddControllersWithViews();

// Database
var dbPath = builder.Configuration.GetValue<string>("Database:Path") 
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "glimpse", "glimpse.db");
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
builder.Services.AddDbContext<GlimpseDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// Services
builder.Services.AddSingleton<OcrService>();
builder.Services.AddSingleton<ScanProgressService>();
builder.Services.AddHostedService<ScreenshotWatcherService>();

var app = builder.Build();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GlimpseDbContext>();
    db.Database.EnsureCreated();
}

app.UseStaticFiles();

// Serve screenshot files
var watchPath = builder.Configuration.GetValue<string>("Screenshots:WatchPath")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures/Screenshots");
if (Directory.Exists(watchPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(watchPath),
        RequestPath = "/screenshots"
    });
}

app.UseRouting();

// SSE endpoint for progress and screenshot updates
app.MapGet("/api/progress/stream", (ScanProgressService progress, CancellationToken ct) =>
    TypedResults.ServerSentEvents(progress.GetUpdatesAsync(ct)));

app.MapControllerRoute(
    name: "detail",
    pattern: "detail/{id}",
    defaults: new { controller = "Home", action = "Detail" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
