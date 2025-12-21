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

    public async Task<IActionResult> Index(string? q, int page = 0)
    {
        var query = _db.Screenshots.AsQueryable();
        
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
                query = query.Where(s => s.OcrText != null && EF.Functions.Like(s.OcrText, $"%{q}%"));
            }
        }
        
        var screenshots = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip(page * PageSize)
            .Take(PageSize)
            .ToListAsync();

        ViewBag.SearchQuery = q;
        ViewBag.Progress = _progress;
        ViewBag.Page = page;
        ViewBag.HasMore = screenshots.Count == PageSize;
        
        if (Request.Headers.XRequestedWith == "XMLHttpRequest")
        {
            return PartialView("_ScreenshotGrid", screenshots);
        }
        
        return View(screenshots);
    }

    public async Task<IActionResult> Detail(int id, string? q)
    {
        var screenshot = await _db.Screenshots.FindAsync(id);
        if (screenshot == null) return NotFound();
        
        ViewBag.ReturnQuery = q;
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
