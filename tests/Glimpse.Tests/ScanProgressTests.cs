using Glimpse.Services;

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
}
