using Glimpse.Data;
using Glimpse.Models;
using Glimpse.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Glimpse.Controllers;

public class HomeController(GlimpseDbContext db, ScanProgressService progress) : Controller
{
    private const int PageSize = 24;

    /// <summary>
    /// Search page. XHR requests with <paramref name="before"/> return the next page of cards as a partial,
    /// with the cursor for the page after that in the X-Next-Cursor header (absent when there are no more).
    /// </summary>
    public async Task<IActionResult> Index(string? q, string? tag, string? before = null)
    {
        var query = db.Screenshots.Include(s => s.Tags).Search(q, tag);
        var isPartial = Request.Headers.XRequestedWith == "XMLHttpRequest";

        var page = query;
        if (PageCursor.TryParse(before, out var cursor))
        {
            page = page.After(cursor);
        }

        // Fetch one extra row to find out whether there's another page without a second COUNT query.
        var screenshots = await page.NewestFirst().Take(PageSize + 1).ToListAsync();
        var hasMore = screenshots.Count > PageSize;
        if (hasMore) screenshots.RemoveAt(PageSize);
        var nextCursor = hasMore ? PageCursor.From(screenshots[^1]).ToString() : null;

        ViewBag.SearchQuery = q;

        if (isPartial)
        {
            if (nextCursor != null) Response.Headers["X-Next-Cursor"] = nextCursor;
            return PartialView("_ScreenshotGrid", screenshots);
        }

        ViewBag.CurrentTag = tag;
        ViewBag.NextCursor = nextCursor;
        ViewBag.TotalCount = await query.CountAsync();
        ViewBag.AllTags = await db.Tags.OrderBy(t => t.Name).ToListAsync();
        ViewBag.Progress = progress;

        return View(screenshots);
    }

    /// <summary>A single screenshot card, used to insert or refresh cards live from SSE events.</summary>
    public async Task<IActionResult> Card(int id, string? q)
    {
        var screenshot = await db.Screenshots
            .Include(s => s.Tags)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (screenshot == null) return NotFound();

        ViewBag.SearchQuery = q;
        return PartialView("_ScreenshotGrid", new List<Screenshot> { screenshot });
    }

    public async Task<IActionResult> Detail(int id, string? q)
    {
        var screenshot = await db.Screenshots
            .Include(s => s.Tags)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (screenshot == null) return NotFound();
        ViewBag.ReturnQuery = q;

        // Find previous and next screenshots in a single query
        var neighbours = await db.Database
            .SqlQuery<NeighbourResult>($"""
                SELECT * FROM (
                    SELECT 'prev' as Direction, Id, Path FROM Screenshots
                    WHERE CreatedAt < {screenshot.CreatedAt} OR (CreatedAt = {screenshot.CreatedAt} AND Id < {screenshot.Id})
                    ORDER BY CreatedAt DESC, Id DESC LIMIT 1
                )
                UNION ALL
                SELECT * FROM (
                    SELECT 'next' as Direction, Id, Path FROM Screenshots
                    WHERE CreatedAt > {screenshot.CreatedAt} OR (CreatedAt = {screenshot.CreatedAt} AND Id > {screenshot.Id})
                    ORDER BY CreatedAt ASC, Id ASC LIMIT 1
                )
                """)
            .ToListAsync();

        var prev = neighbours.FirstOrDefault(n => n.Direction == "prev");
        var next = neighbours.FirstOrDefault(n => n.Direction == "next");

        ViewBag.PrevId = prev?.Id;
        ViewBag.NextId = next?.Id;
        ViewBag.PrevPath = prev?.Path;
        ViewBag.NextPath = next?.Path;

        return View(screenshot);
    }

    private record NeighbourResult(string Direction, int Id, string Path);
}
