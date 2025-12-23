using Glimpse.Data;
using Glimpse.Helpers;
using Glimpse.Models;
using Glimpse.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace Glimpse.Controllers;
public class HomeController : Controller
{
    private readonly GlimpseDbContext _db;
    private readonly ScanProgressService _progress;
    private const int PageSize = 24;
    public HomeController(GlimpseDbContext db, ScanProgressService progress)
    {
        _db = db;
        _progress = progress;
    }
    public async Task<IActionResult> Index(string? q, string? tag, int page = 0, string? partial = null, int? id = null, string? exclude = null)
    {
        // Handle single card partial request
        if (partial == "card" && id.HasValue)
        {
            var screenshot = await _db.Screenshots
                .Include(s => s.Tags)
                .FirstOrDefaultAsync(s => s.Id == id.Value);
            if (screenshot == null) return NotFound();
            ViewBag.SearchQuery = q;
            return PartialView("_ScreenshotGrid", new List<Screenshot> { screenshot });
        }

        var query = _db.Screenshots.Include(s => s.Tags).AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            // Try to parse as date
            var dateRange = DateHelper.TryParseDate(q);
            if (dateRange.HasValue)
            {
                var (start, end) = dateRange.Value;
                query = query.Where(s => s.CreatedAt >= start && s.CreatedAt < end);
            }
            else
            {
                // Text search
                query = query.Where(s =>
                    (s.OcrText != null && EF.Functions.Like(s.OcrText, $"%{q}%")) ||
                    (s.Notes != null && EF.Functions.Like(s.Notes, $"%{q}%")));
            }
        }

        // Tag filter
        if (!string.IsNullOrWhiteSpace(tag))
        {
            query = query.Where(s => s.Tags.Any(t => t.Name == tag.ToLowerInvariant()));
        }

        // Exclude already-rendered IDs (for deduplication during pagination)
        if (!string.IsNullOrWhiteSpace(exclude))
        {
            var excludeIds = exclude.Split(',')
                .Select(x => int.TryParse(x, out var i) ? i : 0)
                .Where(x => x > 0)
                .ToHashSet();
            if (excludeIds.Count > 0)
            {
                query = query.Where(s => !excludeIds.Contains(s.Id));
            }
        }

        var totalCount = await query.CountAsync();

        var screenshots = await query
            .OrderByDescending(s => s.CreatedAt)
            .ThenByDescending(s => s.Id)
            .Skip(page * PageSize)
            .Take(PageSize)
            .ToListAsync();

        ViewBag.SearchQuery = q;
        ViewBag.CurrentTag = tag;
        ViewBag.CurrentPage = page;
        ViewBag.HasMore = (page + 1) * PageSize < totalCount;
        ViewBag.TotalCount = totalCount;
        ViewBag.AllTags = await _db.Tags.OrderBy(t => t.Name).ToListAsync();
        ViewBag.Progress = _progress;

        if (Request.Headers.XRequestedWith == "XMLHttpRequest")
        {
            Response.Headers["X-Has-More"] = ViewBag.HasMore.ToString().ToLower();
            Response.Headers["X-Total-Count"] = totalCount.ToString();
            return PartialView("_ScreenshotGrid", screenshots);
        }

        return View(screenshots);
    }
    public async Task<IActionResult> Detail(int id, string? q)
    {
        var screenshot = await _db.Screenshots
            .Include(s => s.Tags)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (screenshot == null) return NotFound();
        ViewBag.ReturnQuery = q;
        ViewBag.ImageWidth = screenshot.Width;
        ViewBag.ImageHeight = screenshot.Height;

        // Find previous and next screenshots in a single query
        var neighbours = await _db.Database
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
        ViewBag.PrevFilename = prev != null ? Path.GetFileName(prev.Path) : null;
        ViewBag.NextFilename = next != null ? Path.GetFileName(next.Path) : null;

        return View(screenshot);
    }

    private record NeighbourResult(string Direction, int Id, string Path);
}
