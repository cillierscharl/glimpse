using System.Diagnostics;
using System.Threading.Channels;
using Glimpse.Data;
using Glimpse.Helpers;
using Glimpse.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

namespace Glimpse.Services;

public class ScreenshotWatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OcrService _ocrService;
    private readonly ScanProgressService _progress;
    private readonly ILogger<ScreenshotWatcherService> _logger;
    private readonly string _watchPath;
    private readonly string[] _extensions;
    private readonly bool _metricsEnabled;
    private readonly Channel<string> _priorityQueue = Channel.CreateUnbounded<string>();
    private readonly Channel<string> _backlogQueue = Channel.CreateUnbounded<string>();
    private PhysicalFileProvider? _fileProvider;

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
        _watchPath = config.GetWatchPath();
        _extensions = config.GetSection("Screenshots:Extensions").Get<string[]>() ?? [".png", ".jpg", ".jpeg"];
        _metricsEnabled = config.GetValue<bool>("Metrics:Enabled");
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

        // Start watching for new files using PhysicalFileProvider (supports polling via DOTNET_USE_POLLING_FILE_WATCHER)
        _fileProvider = new PhysicalFileProvider(_watchPath);
        _ = WatchForNewFilesAsync(stoppingToken);

        _logger.LogInformation("Watching for screenshots in: {Path} (polling: {Polling})",
            _watchPath,
            Environment.GetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER") == "true" ? "enabled" : "disabled");

        if (_metricsEnabled)
        {
            _ = UpdateMetricsAsync(stoppingToken);
        }

        await ProcessBacklogAsync(stoppingToken);

        // Continue listening for new files
        await foreach (var file in _priorityQueue.Reader.ReadAllAsync(stoppingToken))
        {
            _progress.CurrentFile = Path.GetFileName(file);
            _progress.NotifyChange();
            await ProcessImageAsync(file, stoppingToken, notify: true);
        }
    }

    private async Task WatchForNewFilesAsync(CancellationToken ct)
    {
        var knownFiles = new HashSet<string>(
            Directory.EnumerateFiles(_watchPath).Where(IsValidExtension));

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Watch for any file changes in the directory
                var changeToken = _fileProvider!.Watch("*.*");
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var registration = changeToken.RegisterChangeCallback(_ => tcs.TrySetResult(true), null);
                await tcs.Task.WaitAsync(ct);

                // Small delay to let file writes complete
                await Task.Delay(500, ct);

                // Diffing against the previous listing means each new file is reported exactly once,
                // however many change notifications its creation triggers.
                var currentFiles = Directory.EnumerateFiles(_watchPath)
                    .Where(IsValidExtension)
                    .ToHashSet();

                foreach (var filePath in currentFiles.Except(knownFiles))
                {
                    await OnNewFileDetectedAsync(filePath);
                }

                knownFiles = currentFiles;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in file watcher loop");
                await Task.Delay(1000, ct);
            }
        }
    }

    private async Task OnNewFileDetectedAsync(string filePath)
    {
        try
        {
            // Insert screenshot record immediately as Pending so it shows up in the UI
            var screenshot = await InsertPendingScreenshotAsync(filePath);
            if (screenshot != null)
            {
                GlimpseMetrics.ScreenshotsDetected.Inc();
                _logger.LogInformation("New screenshot detected and saved: {Path} (ID: {Id})", filePath, screenshot.Id);
                _progress.NotifyScreenshotDetected(screenshot.Id, Path.GetFileName(filePath));
            }

            // Queue for OCR processing
            await _priorityQueue.Writer.WriteAsync(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling new file: {Path}", filePath);
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

    /// <summary>
    /// Queues everything that still needs OCR (files not yet in the database, plus rows left unfinished
    /// by a previous run) and processes it with two workers, giving newly detected files priority.
    /// </summary>
    private async Task ProcessBacklogAsync(CancellationToken stoppingToken)
    {
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GlimpseDbContext>();

            // Pending/Processing rows mean the app stopped mid-run. Failed rows are retried too, since the
            // usual cause is transient (Ollama restarting or running out of memory).
            var unfinished = await db.Screenshots
                .Where(s => s.Status != ScreenshotStatus.Completed)
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => s.Path)
                .ToListAsync(stoppingToken);

            var existingPaths = (await db.Screenshots.Select(s => s.Path).ToListAsync(stoppingToken)).ToHashSet();

            var allFiles = Directory.EnumerateFiles(_watchPath)
                .Where(IsValidExtension)
                .ToList();

            var newFiles = allFiles
                .Where(f => !existingPaths.Contains(f))
                .OrderByDescending(File.GetCreationTimeUtc)
                .ToList();

            _progress.TotalFiles = allFiles.Count;
            _progress.AlreadyIndexed = existingPaths.Count - unfinished.Count;
            _progress.ProcessedFiles = 0;
            _progress.IsScanning = newFiles.Count + unfinished.Count > 0;

            _logger.LogInformation("Found {Count} new screenshots to process, {Unfinished} unfinished ({Indexed} already indexed)",
                newFiles.Count, unfinished.Count, _progress.AlreadyIndexed);

            // Insert all new files as Pending immediately so they show in UI
            foreach (var file in newFiles)
            {
                db.Screenshots.Add(new Screenshot
                {
                    Path = file,
                    Status = ScreenshotStatus.Pending,
                    CreatedAt = File.GetCreationTimeUtc(file)
                });
            }
            if (newFiles.Count > 0)
            {
                await db.SaveChangesAsync(stoppingToken);
                _logger.LogInformation("Inserted {Count} pending screenshots", newFiles.Count);
            }

            // Unfinished screenshots first (recovery from crash), then the new files
            foreach (var file in unfinished.Where(File.Exists).Concat(newFiles))
            {
                await _backlogQueue.Writer.WriteAsync(file, stoppingToken);
            }
            _backlogQueue.Writer.Complete();
        }

        await Task.WhenAll(
            ProcessWorkerAsync(stoppingToken),
            ProcessWorkerAsync(stoppingToken)
        );

        _progress.IsScanning = false;
        _progress.CurrentFile = null;
        _progress.NotifyChange();
        _logger.LogInformation("Finished processing backlog");
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
            _progress.IncrementProcessed();
            _progress.NotifyChange();
        }
    }

    private async Task ProcessImageAsync(string path, CancellationToken cancellationToken, bool notify)
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

            var stopwatch = Stopwatch.StartNew();
            var text = await _ocrService.ExtractTextAsync(path, cancellationToken);
            GlimpseMetrics.OcrDuration.Observe(stopwatch.Elapsed.TotalSeconds);

            // Capture image dimensions (Identify only reads the header, unlike Load which decodes every pixel)
            try
            {
                var info = await SixLabors.ImageSharp.Image.IdentifyAsync(path, cancellationToken);
                screenshot.Width = info.Width;
                screenshot.Height = info.Height;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to read image dimensions: {Path}", path);
            }

            // Update with OCR result
            screenshot.OcrText = text;
            screenshot.Status = ScreenshotStatus.Completed;
            await db.SaveChangesAsync(cancellationToken);

            _progress.NotifyScreenshotStatusChanged(screenshot.Id, ScreenshotStatus.Completed, text);

            GlimpseMetrics.ScreenshotsProcessed.WithLabels("success").Inc();

            if (notify)
            {
                _progress.NotifyScreenshotIndexed(screenshot.Id, Path.GetFileName(path));
            }
            _logger.LogInformation("Indexed: {Path} ({Length} chars)", path, text.Length);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down: leave the row as Processing so it's picked up again on the next start.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process: {Path}", path);
            GlimpseMetrics.ScreenshotsProcessed.WithLabels("failed").Inc();

            // Mark as failed. Start from a clean context: the failure may have come from SaveChanges itself.
            db.ChangeTracker.Clear();
            var screenshot = await db.Screenshots.FirstOrDefaultAsync(s => s.Path == path, cancellationToken);
            if (screenshot != null && screenshot.Status != ScreenshotStatus.Completed)
            {
                screenshot.Status = ScreenshotStatus.Failed;
                await db.SaveChangesAsync(cancellationToken);
                _progress.NotifyScreenshotStatusChanged(screenshot.Id, ScreenshotStatus.Failed);
            }
        }
    }

    private async Task UpdateMetricsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<GlimpseDbContext>();

                var total = await db.Screenshots.CountAsync(ct);
                var pending = await db.Screenshots.CountAsync(s =>
                    s.Status == ScreenshotStatus.Pending || s.Status == ScreenshotStatus.Processing, ct);

                GlimpseMetrics.ScreenshotsTotal.Set(total);
                GlimpseMetrics.ScreenshotsPending.Set(pending);
                GlimpseMetrics.QueueSize.WithLabels("priority").Set(_priorityQueue.Reader.Count);
                GlimpseMetrics.QueueSize.WithLabels("backlog").Set(_backlogQueue.Reader.CanCount ? _backlogQueue.Reader.Count : 0);

                await Task.Delay(TimeSpan.FromSeconds(15), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to update metrics");
                await Task.Delay(TimeSpan.FromSeconds(15), ct);
            }
        }
    }

    private bool IsValidExtension(string path) =>
        _extensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    public override void Dispose()
    {
        _fileProvider?.Dispose();
        base.Dispose();
    }
}
