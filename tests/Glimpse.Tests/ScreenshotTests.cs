using Glimpse.Data;
using Glimpse.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Glimpse.Tests;

public class ScreenshotTests
{
    private GlimpseDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<GlimpseDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new GlimpseDbContext(options);
    }

    [Fact]
    public async Task CanAddScreenshot()
    {
        using var db = CreateInMemoryDb();
        
        var screenshot = new Screenshot
        {
            Path = "/screenshots/test.png",
            OcrText = "Hello World",
            CreatedAt = DateTime.UtcNow
        };
        
        db.Screenshots.Add(screenshot);
        await db.SaveChangesAsync();
        
        Assert.Equal(1, await db.Screenshots.CountAsync());
    }

    [Fact]
    public async Task CanSearchByOcrText()
    {
        using var db = CreateInMemoryDb();
        
        db.Screenshots.AddRange(
            new Screenshot { Path = "/a.png", OcrText = "Hello World", CreatedAt = DateTime.UtcNow },
            new Screenshot { Path = "/b.png", OcrText = "Goodbye World", CreatedAt = DateTime.UtcNow },
            new Screenshot { Path = "/c.png", OcrText = "No match here", CreatedAt = DateTime.UtcNow }
        );
        await db.SaveChangesAsync();
        
        var results = await db.Screenshots
            .Where(s => s.OcrText != null && s.OcrText.Contains("World"))
            .ToListAsync();
        
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task CanDeleteScreenshot()
    {
        using var db = CreateInMemoryDb();
        
        var screenshot = new Screenshot
        {
            Path = "/screenshots/delete-me.png",
            OcrText = "Delete me",
            CreatedAt = DateTime.UtcNow
        };
        
        db.Screenshots.Add(screenshot);
        await db.SaveChangesAsync();
        
        db.Screenshots.Remove(screenshot);
        await db.SaveChangesAsync();
        
        Assert.Equal(0, await db.Screenshots.CountAsync());
    }

    [Fact]
    public async Task CanFindNextScreenshotByDate()
    {
        using var db = CreateInMemoryDb();
        
        var now = DateTime.UtcNow;
        var s1 = new Screenshot { Path = "/1.png", OcrText = "", CreatedAt = now.AddHours(-3) };
        var s2 = new Screenshot { Path = "/2.png", OcrText = "", CreatedAt = now.AddHours(-2) };
        var s3 = new Screenshot { Path = "/3.png", OcrText = "", CreatedAt = now.AddHours(-1) };
        
        db.Screenshots.AddRange(s1, s2, s3);
        await db.SaveChangesAsync();
        
        // Find next after s3 (should be s2)
        var next = await db.Screenshots
            .Where(s => s.CreatedAt < s3.CreatedAt)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();
        
        Assert.NotNull(next);
        Assert.Equal(s2.Path, next.Path);
    }
}
