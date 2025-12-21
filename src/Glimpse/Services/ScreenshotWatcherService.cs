using System.Collections.Concurrent;
using Glimpse.Data;
using Glimpse.Models;
using Microsoft.EntityFrameworkCore;

namespace Glimpse.Services;

public class ScreenshotWatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OcrService _ocrService;
    private readonly ScanProgressService _progress;
    private readonly ILogger<ScreenshotWatcherService> _logger;
    private readonly string _watchPath;
    private readonly string[] _extensions;
    private readonly ConcurrentDictionary<string, DateTime> _recentFiles = new();
    private FileSystemWatcher? _watcher;

    public ScreenshotWatcherService(
        IServiceScopeFactory scopeFactory,
        OcrService ocrService,
        ScanProgressService progress,
        IConfiguration config,
        ILogger<ScreenshotWatcherService> logger)
    {
        _scopeFactory = scopeFactory;
        _ocrService = ocrService;
        _progress = progress;
        _logger = logger;
        _watchPath = config.GetValue<string>("Screenshots:WatchPath") 
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures/Screenshots");
        _extensions = config.GetSection("Screenshots:Extensions").Get<string[]>() ?? [".png", ".jpg", ".jpeg"];
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Directory.Exists(_watchPath))
        {
            Directory.CreateDirectory(_watchPath);
            _logger.LogInformation("Created watch directory: {Path}", _watchPath);
        }

        // Start watching for new files immediately
        _watcher = new FileSystemWatcher(_watchPath)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnFileCreated;

        _logger.LogInformation("Watching for screenshots in: {Path}", _watchPath);

        // Process existing images in background (don't block startup)
        _ = Task.Run(() => ProcessExistingImagesAsync(stoppingToken), stoppingToken);

        // Keep the service running
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        if (!IsValidExtension(e.FullPath)) return;

        // Debounce: ignore if we've seen this file recently
        var now = DateTime.UtcNow;
        if (_recentFiles.TryGetValue(e.FullPath, out var lastSeen) && (now - lastSeen).TotalSeconds < 2)
            return;
        _recentFiles[e.FullPath] = now;

        // Wait for file to be fully written
        await Task.Delay(500);
        await ProcessImageAsync(e.FullPath);
    }

    private async Task ProcessExistingImagesAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GlimpseDbContext>();

        var existingPaths = (await db.Screenshots.Select(s => s.Path).ToListAsync(stoppingToken)).ToHashSet();
        
        var allFiles = Directory.EnumerateFiles(_watchPath)
            .Where(f => IsValidExtension(f))
            .ToList();
            
        var filesToProcess = allFiles.Where(f => !existingPaths.Contains(f)).ToList();

        _progress.TotalFiles = allFiles.Count;
        _progress.AlreadyIndexed = existingPaths.Count;
        _progress.ProcessedFiles = 0;
        _progress.IsScanning = filesToProcess.Count > 0;
        
        _logger.LogInformation("Found {Count} new screenshots to process ({Indexed} already indexed)", 
            filesToProcess.Count, existingPaths.Count);

        // Process in parallel with limited concurrency
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2),
            CancellationToken = stoppingToken
        };

        await Parallel.ForEachAsync(filesToProcess, parallelOptions, async (file, ct) =>
        {
            _progress.CurrentFile = Path.GetFileName(file);
            await ProcessImageAsync(file, ct);
            Interlocked.Increment(ref _progress._processedFiles);
        });
        
        _progress.IsScanning = false;
        _progress.CurrentFile = null;
        _logger.LogInformation("Finished processing all screenshots");
    }

    private async Task ProcessImageAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing: {Path}", path);
            var text = await _ocrService.ExtractTextAsync(path, cancellationToken);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GlimpseDbContext>();

            if (await db.Screenshots.AnyAsync(s => s.Path == path, cancellationToken)) return;

            db.Screenshots.Add(new Screenshot
            {
                Path = path,
                OcrText = text,
                CreatedAt = File.GetCreationTimeUtc(path)
            });
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Indexed: {Path} ({Length} chars)", path, text.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process: {Path}", path);
        }
    }

    private bool IsValidExtension(string path) =>
        _extensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    public override void Dispose()
    {
        _watcher?.Dispose();
        base.Dispose();
    }
}
