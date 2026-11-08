using Glimpse.Data;
using Glimpse.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Glimpse.Tests;

public class ScreenshotQueriesTests : IDisposable
{
    private readonly SqliteTestDb _db = new();

    public ScreenshotQueriesTests()
    {
        using var db = _db.CreateContext();
        var work = new Tag { Name = "work" };
        db.Screenshots.AddRange(
            new Screenshot { Path = "/a.png", OcrText = "Hello World", CreatedAt = new DateTime(2025, 3, 15, 9, 0, 0), Tags = [work] },
            new Screenshot { Path = "/b.png", OcrText = "Unrelated", Notes = "remember to say hello", CreatedAt = new DateTime(2025, 3, 16, 9, 0, 0) },
            new Screenshot { Path = "/c.png", OcrText = "Nothing here", CreatedAt = new DateTime(2025, 3, 15, 23, 0, 0), Tags = [work] });
        db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private async Task<List<string>> SearchAsync(string? q, string? tag = null)
    {
        using var db = _db.CreateContext();
        return await db.Screenshots.Search(q, tag).NewestFirst().Select(s => s.Path).ToListAsync();
    }

    [Fact]
    public async Task Search_MatchesOcrTextAndNotes_CaseInsensitively()
    {
        Assert.Equal(["/b.png", "/a.png"], await SearchAsync("HELLO"));
    }

    [Fact]
    public async Task Search_DateQueryFiltersToThatDay()
    {
        Assert.Equal(["/c.png", "/a.png"], await SearchAsync("2025-03-15"));
    }

    [Fact]
    public async Task Search_FiltersByTag_IgnoringCaseAndWhitespace()
    {
        Assert.Equal(["/c.png", "/a.png"], await SearchAsync(null, " Work "));
    }

    [Fact]
    public async Task Search_CombinesTextAndTag()
    {
        Assert.Equal(["/a.png"], await SearchAsync("hello", "work"));
    }

    [Fact]
    public async Task Search_WithNoFiltersReturnsEverything()
    {
        Assert.Equal(3, (await SearchAsync(" ")).Count);
    }

    [Fact]
    public void PageCursor_RoundTrips()
    {
        var cursor = new PageCursor(new DateTime(2025, 3, 15, 9, 30, 15, 123), 42);

        Assert.True(PageCursor.TryParse(cursor.ToString(), out var parsed));
        Assert.Equal(cursor, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("abc_1")]
    [InlineData("1_abc")]
    [InlineData("-1_1")]
    [InlineData("1_2_3")]
    [InlineData("99999999999999999999_1")]
    [InlineData("9223372036854775807_1")] // > DateTime.MaxValue.Ticks
    public void PageCursor_RejectsMalformedInput(string? value)
    {
        Assert.False(PageCursor.TryParse(value, out _));
    }
}
