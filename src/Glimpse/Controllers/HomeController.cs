using System.Globalization;
using System.Text.RegularExpressions;
using Glimpse.Data;
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

    public async Task<IActionResult> Index(string? q, string? tag, int page = 0, string? partial = null, int? id = null)
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
            var dateRange = TryParseDate(q);
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

        var screenshots = await query
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        ViewBag.SearchQuery = q;
        ViewBag.CurrentTag = tag;
        ViewBag.AllTags = await _db.Tags.OrderBy(t => t.Name).ToListAsync();
        ViewBag.Progress = _progress;

        if (Request.Headers.XRequestedWith == "XMLHttpRequest")
        {
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

        // Use stored dimensions
        ViewBag.ImageWidth = screenshot.Width;
        ViewBag.ImageHeight = screenshot.Height;

        return View(screenshot);
    }

    private (DateTime start, DateTime end)? TryParseDate(string input)
    {
        var now = DateTime.UtcNow;
        
        // Try common date patterns
        string[] formats = [
            "MMMM d", "MMMM dd", "MMM d", "MMM dd",  // November 26, Nov 26
            "d MMMM", "dd MMMM", "d MMM", "dd MMM",  // 26 November, 26 Nov
            "MMMM d, yyyy", "MMM d, yyyy",           // November 26, 2024
            "yyyy-MM-dd", "MM/dd/yyyy", "dd/MM/yyyy" // 2024-11-26
        ];

        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(input, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                // If no year specified, try current year first, then previous year
                if (!format.Contains("yyyy"))
                {
                    date = new DateTime(now.Year, date.Month, date.Day);
                    if (date > now) date = date.AddYears(-1);
                }
                return (date.Date, date.Date.AddDays(1));
            }
        }

        // Try month name only (e.g., "November")
        for (int m = 1; m <= 12; m++)
        {
            var monthName = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(m);
            var shortMonthName = CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(m);
            if (input.Equals(monthName, StringComparison.OrdinalIgnoreCase) ||
                input.Equals(shortMonthName, StringComparison.OrdinalIgnoreCase))
            {
                var year = now.Month >= m ? now.Year : now.Year - 1;
                var start = new DateTime(year, m, 1);
                return (start, start.AddMonths(1));
            }
        }

        return null;
    }

}
