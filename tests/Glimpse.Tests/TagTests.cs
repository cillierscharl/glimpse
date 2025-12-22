using Glimpse.Data;
using Glimpse.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Glimpse.Tests;

public class TagTests
{
    private GlimpseDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<GlimpseDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new GlimpseDbContext(options);
    }

    [Fact]
    public async Task CanAddTagToScreenshot()
    {
        using var db = CreateInMemoryDb();

        var screenshot = new Screenshot { Path = "/test.png", CreatedAt = DateTime.UtcNow };
        var tag = new Tag { Name = "work" };

        screenshot.Tags.Add(tag);
        db.Screenshots.Add(screenshot);
        await db.SaveChangesAsync();

        var loaded = await db.Screenshots
            .Include(s => s.Tags)
            .FirstAsync();

        Assert.Single(loaded.Tags);
        Assert.Equal("work", loaded.Tags.First().Name);
    }

    [Fact]
    public async Task CanAddMultipleTagsToScreenshot()
    {
        using var db = CreateInMemoryDb();

        var screenshot = new Screenshot { Path = "/test.png", CreatedAt = DateTime.UtcNow };
        screenshot.Tags.Add(new Tag { Name = "work" });
        screenshot.Tags.Add(new Tag { Name = "important" });
        screenshot.Tags.Add(new Tag { Name = "meeting" });

        db.Screenshots.Add(screenshot);
        await db.SaveChangesAsync();

        var loaded = await db.Screenshots
            .Include(s => s.Tags)
            .FirstAsync();

        Assert.Equal(3, loaded.Tags.Count);
    }

    [Fact]
    public async Task CanRemoveTagFromScreenshot()
    {
        using var db = CreateInMemoryDb();

        var tag = new Tag { Name = "removeme" };
        var screenshot = new Screenshot { Path = "/test.png", CreatedAt = DateTime.UtcNow };
        screenshot.Tags.Add(tag);

        db.Screenshots.Add(screenshot);
        await db.SaveChangesAsync();

        // Remove tag
        var loaded = await db.Screenshots
            .Include(s => s.Tags)
            .FirstAsync();
        loaded.Tags.Remove(loaded.Tags.First());
        await db.SaveChangesAsync();

        var reloaded = await db.Screenshots
            .Include(s => s.Tags)
            .FirstAsync();

        Assert.Empty(reloaded.Tags);
    }

    [Fact]
    public async Task CanShareTagBetweenScreenshots()
    {
        using var db = CreateInMemoryDb();

        var tag = new Tag { Name = "shared" };
        db.Tags.Add(tag);

        var s1 = new Screenshot { Path = "/1.png", CreatedAt = DateTime.UtcNow };
        var s2 = new Screenshot { Path = "/2.png", CreatedAt = DateTime.UtcNow };

        s1.Tags.Add(tag);
        s2.Tags.Add(tag);

        db.Screenshots.AddRange(s1, s2);
        await db.SaveChangesAsync();

        var loadedTag = await db.Tags
            .Include(t => t.Screenshots)
            .FirstAsync();

        Assert.Equal(2, loadedTag.Screenshots.Count);
    }

    [Fact]
    public async Task CanFilterScreenshotsByTag()
    {
        using var db = CreateInMemoryDb();

        var workTag = new Tag { Name = "work" };
        var personalTag = new Tag { Name = "personal" };

        var s1 = new Screenshot { Path = "/1.png", CreatedAt = DateTime.UtcNow };
        var s2 = new Screenshot { Path = "/2.png", CreatedAt = DateTime.UtcNow };
        var s3 = new Screenshot { Path = "/3.png", CreatedAt = DateTime.UtcNow };

        s1.Tags.Add(workTag);
        s2.Tags.Add(workTag);
        s3.Tags.Add(personalTag);

        db.Screenshots.AddRange(s1, s2, s3);
        await db.SaveChangesAsync();

        var workScreenshots = await db.Screenshots
            .Include(s => s.Tags)
            .Where(s => s.Tags.Any(t => t.Name == "work"))
            .ToListAsync();

        Assert.Equal(2, workScreenshots.Count);
    }

    [Fact]
    public async Task DeletingScreenshotRemovesFromTagRelationship()
    {
        using var db = CreateInMemoryDb();

        var tag = new Tag { Name = "test" };
        var screenshot = new Screenshot { Path = "/test.png", CreatedAt = DateTime.UtcNow };
        screenshot.Tags.Add(tag);

        db.Screenshots.Add(screenshot);
        await db.SaveChangesAsync();

        db.Screenshots.Remove(screenshot);
        await db.SaveChangesAsync();

        // Tag should still exist but have no screenshots
        var loadedTag = await db.Tags
            .Include(t => t.Screenshots)
            .FirstOrDefaultAsync();

        Assert.NotNull(loadedTag);
        Assert.Empty(loadedTag.Screenshots);
    }
}
