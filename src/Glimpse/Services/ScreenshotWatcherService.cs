using System.Collections.Concurrent;
using System.Threading.Channels;
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
    private readonly Channel<string> _priorityQueue = Channel.CreateUnbounded<string>();
    private readonly Channel<string> _backlogQueue = Channel.CreateUnbounded<string>();
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

    /// <summary>
    /// Enqueue a screenshot for (re)processing via the priority queue
    /// </summary>
    public async Task EnqueueForProcessingAsync(string path)
    {
        await _priorityQueue.Writer.WriteAsync(path);
        _logger.LogInformation("Enqueued for reprocessing: {Path}", path);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wire up Ollama status to progress service
        _ocrService.OnStatusChange += () =>
        {
            _progress.OllamaStatus = _ocrService.Status;
            _progress.NotifyChange();
        };

        // Wait for Ollama and model to be ready
        _progress.OllamaStatus = _ocrService.Status;
        await _ocrService.WaitForReadyAsync(stoppingToken);

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

        // Process images from queue
        await ProcessQueueAsync(stoppingToken);
    }

    private async void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        try
        {
            if (!IsValidExtension(e.FullPath)) return;

            // Debounce: ignore if we've seen this file recently
            var now = DateTime.UtcNow;
            if (_recentFiles.TryGetValue(e.FullPath, out var lastSeen) && (now - lastSeen).TotalSeconds < 2)
                return;
            _recentFiles[e.FullPath] = now;

            // Clean up old entries (older than 1 minute)
            var cutoff = now.AddMinutes(-1);
            foreach (var key in _recentFiles.Keys)
            {
                if (_recentFiles.TryGetValue(key, out var time) && time < cutoff)
                    _recentFiles.TryRemove(key, out _);
            }

            // Wait for file to be fully written
            await Task.Delay(500);

            // Insert screenshot record immediately as Pending
            var screenshot = await InsertPendingScreenshotAsync(e.FullPath);
            if (screenshot != null)
            {
                _logger.LogInformation("New screenshot detected and saved: {Path} (ID: {Id})", e.FullPath, screenshot.Id);
                _progress.NotifyScreenshotDetected(screenshot.Id, Path.GetFileName(e.FullPath));
            }

            // Queue for OCR processing
            await _priorityQueue.Writer.WriteAsync(e.FullPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling new file: {Path}", e.FullPath);
        }
    }

    private async Task<Screenshot?> InsertPendingScreenshotAsync(string path)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GlimpseDbContext>();

        if (await db.Screenshots.AnyAsync(s => s.Path == path))
            return null;

        var screenshot = new Screenshot
        {
            Path = path,
            Status = ScreenshotStatus.Pending,
            CreatedAt = File.GetCreationTimeUtc(path)
        };

        db.Screenshots.Add(screenshot);
        await db.SaveChangesAsync();

        return screenshot;
    }

    private async Task ProcessQueueAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GlimpseDbContext>();

        // Find any incomplete screenshots (Pending or Processing) that need to be reprocessed
        // This handles the case where the app crashed mid-processing
        var incompleteScreenshots = await db.Screenshots
            .Where(s => s.Status == ScreenshotStatus.Pending || s.Status == ScreenshotStatus.Processing)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => s.Path)
            .ToListAsync(stoppingToken);

        if (incompleteScreenshots.Count > 0)
        {
            _logger.LogInformation("Found {Count} incomplete screenshots to reprocess", incompleteScreenshots.Count);
        }

        var existingPaths = (await db.Screenshots.Select(s => s.Path).ToListAsync(stoppingToken)).ToHashSet();

        var allFiles = Directory.EnumerateFiles(_watchPath)
            .Where(f => IsValidExtension(f))
            .ToList();

        var backlogFiles = allFiles
            .Where(f => !existingPaths.Contains(f))
            .OrderByDescending(f => File.GetCreationTimeUtc(f))
            .ToList();

        var totalToProcess = backlogFiles.Count + incompleteScreenshots.Count;
        _progress.TotalFiles = allFiles.Count;
        _progress.AlreadyIndexed = existingPaths.Count - incompleteScreenshots.Count;
        _progress.ProcessedFiles = 0;
        _progress.IsScanning = totalToProcess > 0;

        _logger.LogInformation("Found {Count} new screenshots to process, {Incomplete} incomplete ({Indexed} already indexed)",
            backlogFiles.Count, incompleteScreenshots.Count, existingPaths.Count - incompleteScreenshots.Count);

        // Insert all backlog files as Pending immediately so they show in UI
        foreach (var file in backlogFiles)
        {
            var screenshot = new Screenshot
            {
                Path = file,
                Status = ScreenshotStatus.Pending,
                CreatedAt = File.GetCreationTimeUtc(file)
            };
            db.Screenshots.Add(screenshot);
        }
        if (backlogFiles.Count > 0)
        {
            await db.SaveChangesAsync(stoppingToken);
            _logger.LogInformation("Inserted {Count} pending screenshots", backlogFiles.Count);
        }

        // Queue incomplete screenshots first (recovery from crash)
        foreach (var file in incompleteScreenshots)
        {
            if (File.Exists(file))
            {
                await _backlogQueue.Writer.WriteAsync(file, stoppingToken);
            }
        }

        // Queue new backlog files for OCR processing
        foreach (var file in backlogFiles)
        {
            await _backlogQueue.Writer.WriteAsync(file, stoppingToken);
        }
        _backlogQueue.Writer.Complete();

        // Process with 2 parallel workers
        await Task.WhenAll(
            ProcessWorkerAsync(stoppingToken),
            ProcessWorkerAsync(stoppingToken)
        );

        _progress.IsScanning = false;
        _progress.CurrentFile = null;
        _progress.NotifyChange();
        _logger.LogInformation("Finished processing backlog");

        // Continue listening for new files
        await foreach (var file in _priorityQueue.Reader.ReadAllAsync(stoppingToken))
        {
            _progress.CurrentFile = Path.GetFileName(file);
            _progress.NotifyChange();
            await ProcessImageAsync(file, stoppingToken, notify: true);
        }
    }

    private async Task ProcessWorkerAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // Priority: drain all new files first
            while (_priorityQueue.Reader.TryRead(out var priorityFile))
            {
                _progress.CurrentFile = Path.GetFileName(priorityFile);
                _progress.NotifyChange();
                await ProcessImageAsync(priorityFile, ct, notify: true);
                _progress.NotifyChange();
            }

            // Then take one from backlog
            if (!_backlogQueue.Reader.TryRead(out var file))
            {
                break; // Backlog empty
            }

            _progress.CurrentFile = Path.GetFileName(file);
            _progress.NotifyChange();
            await ProcessImageAsync(file, ct, notify: false);
            Interlocked.Increment(ref _progress._processedFiles);
            _progress.NotifyChange();
        }
    }

    private async Task ProcessImageAsync(string path, CancellationToken cancellationToken = default, bool notify = false)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GlimpseDbContext>();

        try
        {
            // Get the existing screenshot record (should already exist as Pending)
            var screenshot = await db.Screenshots.FirstOrDefaultAsync(s => s.Path == path, cancellationToken);

            if (screenshot == null)
            {
                // Shouldn't happen normally, but handle gracefully
                screenshot = new Screenshot
                {
                    Path = path,
                    Status = ScreenshotStatus.Processing,
                    CreatedAt = File.GetCreationTimeUtc(path)
                };
                db.Screenshots.Add(screenshot);
                await db.SaveChangesAsync(cancellationToken);
            }

            // Skip if already completed
            if (screenshot.Status == ScreenshotStatus.Completed)
                return;

            // Update status to Processing
            screenshot.Status = ScreenshotStatus.Processing;
            await db.SaveChangesAsync(cancellationToken);
            _progress.NotifyScreenshotStatusChanged(screenshot.Id, ScreenshotStatus.Processing);

            _logger.LogInformation("Processing OCR: {Path}", path);

            // Perform OCR
            var text = await _ocrService.ExtractTextAsync(path, cancellationToken);

            // Update with OCR result
            screenshot.OcrText = text;
            screenshot.Status = ScreenshotStatus.Completed;
            await db.SaveChangesAsync(cancellationToken);

            _progress.NotifyScreenshotStatusChanged(screenshot.Id, ScreenshotStatus.Completed, text);

            if (notify)
            {
                _progress.NotifyScreenshotIndexed(screenshot.Id, Path.GetFileName(path));
            }
            _logger.LogInformation("Indexed: {Path} ({Length} chars)", path, text.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process: {Path}", path);

            // Mark as failed
            var screenshot = await db.Screenshots.FirstOrDefaultAsync(s => s.Path == path, cancellationToken);
            if (screenshot != null && screenshot.Status != ScreenshotStatus.Completed)
            {
                screenshot.Status = ScreenshotStatus.Failed;
                await db.SaveChangesAsync(cancellationToken);
                _progress.NotifyScreenshotStatusChanged(screenshot.Id, ScreenshotStatus.Failed);
            }
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
