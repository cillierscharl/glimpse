using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Glimpse.Models;

namespace Glimpse.Services;

public class ScanProgressService
{
    public int TotalFiles { get; set; }
    public int AlreadyIndexed { get; set; }
    public int _processedFiles;
    public int ProcessedFiles
    {
        get => _processedFiles;
        set => _processedFiles = value;
    }
    public volatile bool IsScanning;
    public string? CurrentFile { get; set; }
    public string? OllamaStatus { get; set; }

    public int TotalIndexed => AlreadyIndexed + ProcessedFiles;
    public int RemainingFiles => TotalFiles - TotalIndexed;
    public int PercentComplete => TotalFiles > 0 ? (int)(TotalIndexed * 100.0 / TotalFiles) : 100;

    public event Action? OnProgressChange;
    public event Action<ScreenshotNotification>? OnScreenshotIndexed;
    public event Action<ScreenshotDetectedNotification>? OnScreenshotDetected;
    public event Action<ScreenshotStatusChangedNotification>? OnScreenshotStatusChanged;

    public void NotifyChange() => OnProgressChange?.Invoke();
    public void NotifyScreenshotIndexed(int id, string filename) =>
        OnScreenshotIndexed?.Invoke(new ScreenshotNotification(id, filename));
    public void NotifyScreenshotDetected(int id, string filename) =>
        OnScreenshotDetected?.Invoke(new ScreenshotDetectedNotification(id, filename));
    public void NotifyScreenshotStatusChanged(int id, ScreenshotStatus status, string? ocrText = null) =>
        OnScreenshotStatusChanged?.Invoke(new ScreenshotStatusChangedNotification(id, status, ocrText));

    public async IAsyncEnumerable<SseItem<string>> GetUpdatesAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Send initial progress state
        yield return new SseItem<string>(JsonSerializer.Serialize(GetCurrentUpdate()), "progress");

        while (!ct.IsCancellationRequested)
        {
            var tcs = new TaskCompletionSource<SseItem<string>>();

            Action progressHandler = () =>
                tcs.TrySetResult(new SseItem<string>(JsonSerializer.Serialize(GetCurrentUpdate()), "progress"));
            Action<ScreenshotNotification> screenshotHandler = (s) =>
                tcs.TrySetResult(new SseItem<string>(JsonSerializer.Serialize(s), "screenshot"));
            Action<ScreenshotDetectedNotification> detectedHandler = (s) =>
                tcs.TrySetResult(new SseItem<string>(JsonSerializer.Serialize(s), "screenshot-detected"));
            Action<ScreenshotStatusChangedNotification> statusHandler = (s) =>
                tcs.TrySetResult(new SseItem<string>(JsonSerializer.Serialize(s), "screenshot-status"));

            OnProgressChange += progressHandler;
            OnScreenshotIndexed += screenshotHandler;
            OnScreenshotDetected += detectedHandler;
            OnScreenshotStatusChanged += statusHandler;

            SseItem<string>? result = null;
            try
            {
                result = await tcs.Task.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            finally
            {
                OnProgressChange -= progressHandler;
                OnScreenshotIndexed -= screenshotHandler;
                OnScreenshotDetected -= detectedHandler;
                OnScreenshotStatusChanged -= statusHandler;
            }

            if (result.HasValue) yield return result.Value;
        }
    }

    private ProgressUpdate GetCurrentUpdate() => new(
        IsScanning,
        TotalFiles,
        AlreadyIndexed,
        ProcessedFiles,
        RemainingFiles,
        PercentComplete,
        CurrentFile,
        OllamaStatus
    );
}

public record ProgressUpdate(
    bool IsScanning,
    int TotalFiles,
    int AlreadyIndexed,
    int ProcessedFiles,
    int RemainingFiles,
    int PercentComplete,
    string? CurrentFile,
    string? OllamaStatus
);

public record ScreenshotNotification(int Id, string Filename);
public record ScreenshotDetectedNotification(int Id, string Filename);
public record ScreenshotStatusChangedNotification(int Id, ScreenshotStatus Status, string? OcrText);
