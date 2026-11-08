using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Glimpse.Models;

namespace Glimpse.Services;

public class ScanProgressService
{
    private int _processedFiles;

    public int TotalFiles { get; set; }
    public int AlreadyIndexed { get; set; }
    public int ProcessedFiles
    {
        get => Volatile.Read(ref _processedFiles);
        set => Volatile.Write(ref _processedFiles, value);
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

    /// <summary>Called by OCR workers, which run concurrently.</summary>
    public void IncrementProcessed() => Interlocked.Increment(ref _processedFiles);

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
        // Each connection gets its own queue, subscribed for the whole connection. Events often fire in
        // bursts (e.g. status -> Completed immediately followed by "indexed"), and anything raised while
        // the previous event is still being written must be queued rather than dropped. The queue is
        // bounded so a stalled client can't grow memory without limit.
        var channel = Channel.CreateBounded<SseItem<string>>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

        void Send<T>(T payload, string eventType) =>
            channel.Writer.TryWrite(new SseItem<string>(JsonSerializer.Serialize(payload), eventType));

        Action progressHandler = () => Send(GetSnapshot(), "progress");
        Action<ScreenshotNotification> indexedHandler = s => Send(s, "screenshot");
        Action<ScreenshotDetectedNotification> detectedHandler = s => Send(s, "screenshot-detected");
        Action<ScreenshotStatusChangedNotification> statusHandler = s => Send(s, "screenshot-status");

        OnProgressChange += progressHandler;
        OnScreenshotIndexed += indexedHandler;
        OnScreenshotDetected += detectedHandler;
        OnScreenshotStatusChanged += statusHandler;
        try
        {
            yield return new SseItem<string>(JsonSerializer.Serialize(GetSnapshot()), "progress");

            while (true)
            {
                try
                {
                    if (!await channel.Reader.WaitToReadAsync(ct)) break;
                }
                catch (OperationCanceledException)
                {
                    break; // client disconnected
                }

                while (channel.Reader.TryRead(out var item))
                {
                    yield return item;
                }
            }
        }
        finally
        {
            OnProgressChange -= progressHandler;
            OnScreenshotIndexed -= indexedHandler;
            OnScreenshotDetected -= detectedHandler;
            OnScreenshotStatusChanged -= statusHandler;
        }
    }

    public ProgressUpdate GetSnapshot() => new(
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
