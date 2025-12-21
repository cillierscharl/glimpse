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

            // Wait for file to be fully written, then add to priority queue
            await Task.Delay(500);
            _logger.LogInformation("New screenshot detected, adding to priority queue: {Path}", e.FullPath);
            await _priorityQueue.Writer.WriteAsync(e.FullPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling new file: {Path}", e.FullPath);
        }
    }

    private async Task ProcessQueueAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GlimpseDbContext>();

        var existingPaths = (await db.Screenshots.Select(s => s.Path).ToListAsync(stoppingToken)).ToHashSet();

        var allFiles = Directory.EnumerateFiles(_watchPath)
            .Where(f => IsValidExtension(f))
            .ToList();

        var backlogFiles = allFiles
            .Where(f => !existingPaths.Contains(f))
            .OrderByDescending(f => File.GetCreationTimeUtc(f))
            .ToList();

        _progress.TotalFiles = allFiles.Count;
        _progress.AlreadyIndexed = existingPaths.Count;
        _progress.ProcessedFiles = 0;
        _progress.IsScanning = backlogFiles.Count > 0;

        _logger.LogInformation("Found {Count} new screenshots to process ({Indexed} already indexed)",
            backlogFiles.Count, existingPaths.Count);

        // Queue backlog files
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
        try
        {
            _logger.LogInformation("Processing: {Path}", path);
            var text = await _ocrService.ExtractTextAsync(path, cancellationToken);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GlimpseDbContext>();

            if (await db.Screenshots.AnyAsync(s => s.Path == path, cancellationToken)) return;

            var screenshot = new Screenshot
            {
                Path = path,
                OcrText = text,
                CreatedAt = File.GetCreationTimeUtc(path)
            };
            db.Screenshots.Add(screenshot);
            await db.SaveChangesAsync(cancellationToken);

            if (notify)
            {
                _progress.NotifyScreenshotIndexed(screenshot.Id, Path.GetFileName(path));
            }
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
