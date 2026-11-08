using Glimpse.Models;
using Glimpse.Services;
using Xunit;

namespace Glimpse.Tests;

public class ScanProgressTests
{
    [Fact]
    public void PercentComplete_WithNoFiles_Returns100()
    {
        var progress = new ScanProgressService
        {
            TotalFiles = 0,
            AlreadyIndexed = 0,
            ProcessedFiles = 0
        };

        Assert.Equal(100, progress.PercentComplete);
    }

    [Fact]
    public void PercentComplete_HalfDone_Returns50()
    {
        var progress = new ScanProgressService
        {
            TotalFiles = 100,
            AlreadyIndexed = 40,
            ProcessedFiles = 10
        };

        Assert.Equal(50, progress.PercentComplete);
    }

    [Fact]
    public void PercentComplete_AllIndexed_Returns100()
    {
        var progress = new ScanProgressService
        {
            TotalFiles = 100,
            AlreadyIndexed = 100,
            ProcessedFiles = 0
        };

        Assert.Equal(100, progress.PercentComplete);
    }

    [Fact]
    public void RemainingFiles_CalculatesCorrectly()
    {
        var progress = new ScanProgressService
        {
            TotalFiles = 100,
            AlreadyIndexed = 60,
            ProcessedFiles = 20
        };

        Assert.Equal(20, progress.RemainingFiles);
    }

    [Fact]
    public void TotalIndexed_SumsCorrectly()
    {
        var progress = new ScanProgressService
        {
            TotalFiles = 100,
            AlreadyIndexed = 60,
            ProcessedFiles = 20
        };

        Assert.Equal(80, progress.TotalIndexed);
    }

    [Fact]
    public async Task UpdateStream_DeliversBackToBackEvents()
    {
        var progress = new ScanProgressService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var updates = progress.GetUpdatesAsync(cts.Token).GetAsyncEnumerator(cts.Token);

        Assert.True(await updates.MoveNextAsync());
        Assert.Equal("progress", updates.Current.EventType); // initial snapshot

        // What the OCR worker does on completion: two events with nothing in between.
        progress.NotifyScreenshotStatusChanged(1, ScreenshotStatus.Completed, "text");
        progress.NotifyScreenshotIndexed(1, "a.png");
        progress.NotifyChange();

        var received = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            Assert.True(await updates.MoveNextAsync());
            received.Add(updates.Current.EventType!);
        }
        Assert.Equal(["screenshot-status", "screenshot", "progress"], received);
    }

    [Fact]
    public async Task UpdateStream_UnsubscribesWhenClientDisconnects()
    {
        var progress = new ScanProgressService();
        using var cts = new CancellationTokenSource();
        var updates = progress.GetUpdatesAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        Assert.True(await updates.MoveNextAsync());

        var pending = updates.MoveNextAsync();
        cts.Cancel();

        Assert.False(await pending);
        await updates.DisposeAsync();
        progress.NotifyChange(); // must not throw or deliver anywhere
    }
}
