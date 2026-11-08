using Glimpse.Controllers;
using Glimpse.Models;
using Glimpse.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Glimpse.Tests;

public class HomeControllerTests : IDisposable
{
    private static readonly DateTime T0 = new(2025, 1, 1);
    private readonly SqliteTestDb _db = new();

    public void Dispose() => _db.Dispose();

    private void Seed(int count, Func<int, DateTime> createdAt)
    {
        using var db = _db.CreateContext();
        for (var i = 0; i < count; i++)
        {
            db.Screenshots.Add(new Screenshot
            {
                Path = $"/screenshots/{i}.png",
                CreatedAt = createdAt(i),
                Status = ScreenshotStatus.Completed
            });
        }
        db.SaveChanges();
    }

    private HomeController CreateController(bool xhr)
    {
        var controller = new HomeController(_db.CreateContext(), new ScanProgressService())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        if (xhr) controller.Request.Headers.XRequestedWith = "XMLHttpRequest";
        return controller;
    }

    /// <summary>Loads one page the way the infinite scroll does.</summary>
    private async Task<(List<Screenshot> Page, string? NextCursor)> LoadPageAsync(string? before)
    {
        var controller = CreateController(xhr: true);
        var result = Assert.IsType<PartialViewResult>(await controller.Index(null, null, before));
        var next = controller.Response.Headers["X-Next-Cursor"].ToString();
        return ((List<Screenshot>)result.Model!, next == "" ? null : next);
    }

    private async Task<List<int>> LoadAllPagesAsync(Action<int>? betweenPages = null)
    {
        var seen = new List<int>();
        string? cursor = null;
        var pageNumber = 0;
        do
        {
            var (page, next) = await LoadPageAsync(cursor);
            seen.AddRange(page.Select(s => s.Id));
            cursor = next;
            betweenPages?.Invoke(pageNumber++);
        } while (cursor != null);
        return seen;
    }

    [Fact]
    public async Task InfiniteScroll_ReturnsEveryScreenshotExactlyOnce_NewestFirst()
    {
        // Groups of 5 share a timestamp, so page boundaries (every 24) fall inside a tie.
        Seed(60, i => T0.AddMinutes(i / 5));

        var seen = await LoadAllPagesAsync();

        Assert.Equal(60, seen.Count);
        Assert.Equal(60, seen.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 60).Reverse(), seen); // newest first, Id breaks ties
    }

    [Fact]
    public async Task InfiniteScroll_IsNotDisturbedByScreenshotsAddedOrDeletedBetweenPages()
    {
        Seed(60, i => T0.AddMinutes(i));

        var seen = await LoadAllPagesAsync(betweenPages: page =>
        {
            if (page != 0) return;
            using var db = _db.CreateContext();
            // A new screenshot arrives (shown via SSE, not paging) and the cursor row itself is deleted.
            db.Screenshots.Add(new Screenshot { Path = "/screenshots/new.png", CreatedAt = T0.AddDays(1) });
            db.Screenshots.Remove(db.Screenshots.Single(s => s.Id == 37)); // last item of the first page
            db.SaveChanges();
        });

        Assert.Equal(Enumerable.Range(1, 60).Reverse(), seen);
    }

    [Fact]
    public async Task Index_FullPage_ReportsTotalAndCursorOnlyWhenThereIsMore()
    {
        Seed(24, i => T0.AddMinutes(i));

        var result = Assert.IsType<ViewResult>(await CreateController(xhr: false).Index(null, null));

        Assert.Equal(24, ((List<Screenshot>)result.Model!).Count);
        Assert.Equal(24, result.ViewData["TotalCount"]);
        Assert.Null(result.ViewData["NextCursor"]);
    }

    [Fact]
    public async Task Index_IgnoresMalformedCursor()
    {
        Seed(3, i => T0.AddMinutes(i));

        var (page, next) = await LoadPageAsync("not-a-cursor");

        Assert.Equal(3, page.Count);
        Assert.Null(next);
    }

    [Fact]
    public async Task Card_ReturnsSingleScreenshot()
    {
        Seed(2, i => T0.AddMinutes(i));

        var result = Assert.IsType<PartialViewResult>(await CreateController(xhr: false).Card(2, "query"));

        Assert.Equal(2, Assert.Single((List<Screenshot>)result.Model!).Id);
        Assert.Equal("query", result.ViewData["SearchQuery"]);
    }

    [Fact]
    public async Task Card_ReturnsNotFoundForUnknownId()
    {
        Assert.IsType<NotFoundResult>(await CreateController(xhr: false).Card(999, null));
    }
}
