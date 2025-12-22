using System.Globalization;
using Glimpse.Data;
using Glimpse.Models;
using Glimpse.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Glimpse.Controllers;

[ApiController]
[Route("api")]
[Produces("application/json")]
public class ApiController : ControllerBase
{
    private readonly GlimpseDbContext _db;
    private readonly ScanProgressService _progress;
    private readonly ScreenshotWatcherService _watcher;

    public ApiController(GlimpseDbContext db, ScanProgressService progress, ScreenshotWatcherService watcher)
    {
        _db = db;
        _progress = progress;
        _watcher = watcher;
    }

    /// <summary>
    /// Search screenshots by text content or date
    /// </summary>
    /// <param name="q">Search query (text to find in OCR/notes, or date like "Nov 26", "2024-11-26")</param>
    /// <param name="includeImages">Include base64-encoded images in response (default: false)</param>
    /// <param name="limit">Maximum number of results (default: 24, max: 100)</param>
    /// <param name="offset">Number of results to skip for pagination (default: 0)</param>
    [HttpGet("screenshots/search")]
    public async Task<ActionResult<SearchResponse>> Search(
        [FromQuery] string? q = null,
        [FromQuery] bool includeImages = false,
        [FromQuery] int limit = 24,
        [FromQuery] int offset = 0)
    {
        limit = Math.Clamp(limit, 1, 100);
        offset = Math.Max(0, offset);

        var query = _db.Screenshots.AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var dateRange = TryParseDate(q);
            if (dateRange.HasValue)
            {
                var (start, end) = dateRange.Value;
                query = query.Where(s => s.CreatedAt >= start && s.CreatedAt < end);
            }
            else
            {
                query = query.Where(s =>
                    (s.OcrText != null && EF.Functions.Like(s.OcrText, $"%{q}%")) ||
                    (s.Notes != null && EF.Functions.Like(s.Notes, $"%{q}%")));
            }
        }

        var totalCount = await query.CountAsync();
        var screenshots = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        var results = new List<ScreenshotResult>();
        foreach (var s in screenshots)
        {
            var result = new ScreenshotResult
            {
                Id = s.Id,
                Filename = Path.GetFileName(s.Path),
                OcrText = s.OcrText,
                Notes = s.Notes,
                CreatedAt = s.CreatedAt
            };

            if (System.IO.File.Exists(s.Path))
            {
                try
                {
                    using var stream = System.IO.File.OpenRead(s.Path);
                    using var image = await SixLabors.ImageSharp.Image.LoadAsync(stream);
                    result.Width = image.Width;
                    result.Height = image.Height;

                    if (includeImages)
                    {
                        var bytes = await System.IO.File.ReadAllBytesAsync(s.Path);
                        var ext = Path.GetExtension(s.Path).ToLowerInvariant();
                        var mimeType = ext switch
                        {
                            ".png" => "image/png",
                            ".jpg" or ".jpeg" => "image/jpeg",
                            ".webp" => "image/webp",
                            _ => "application/octet-stream"
                        };
                        result.ImageBase64 = $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
                    }
                }
                catch { /* ignore errors reading image */ }
            }

            results.Add(result);
        }

        return Ok(new SearchResponse
        {
            Query = q,
            TotalCount = totalCount,
            Limit = limit,
            Offset = offset,
            Results = results
        });
    }

    /// <summary>
    /// Get a single screenshot by ID
    /// </summary>
    [HttpGet("screenshots/{id}")]
    public async Task<ActionResult<ScreenshotResult>> GetScreenshot(int id, [FromQuery] bool includeImage = false)
    {
        var s = await _db.Screenshots.FindAsync(id);
        if (s == null) return NotFound();

        var result = new ScreenshotResult
        {
            Id = s.Id,
            Filename = Path.GetFileName(s.Path),
            OcrText = s.OcrText,
            Notes = s.Notes,
            CreatedAt = s.CreatedAt
        };

        if (System.IO.File.Exists(s.Path))
        {
            try
            {
                using var stream = System.IO.File.OpenRead(s.Path);
                using var image = await SixLabors.ImageSharp.Image.LoadAsync(stream);
                result.Width = image.Width;
                result.Height = image.Height;

                if (includeImage)
                {
                    var bytes = await System.IO.File.ReadAllBytesAsync(s.Path);
                    var ext = Path.GetExtension(s.Path).ToLowerInvariant();
                    var mimeType = ext switch
                    {
                        ".png" => "image/png",
                        ".jpg" or ".jpeg" => "image/jpeg",
                        ".webp" => "image/webp",
                        _ => "application/octet-stream"
                    };
                    result.ImageBase64 = $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
                }
            }
            catch { /* ignore errors reading image */ }
        }

        return Ok(result);
    }

    private static (DateTime start, DateTime end)? TryParseDate(string input)
    {
        var now = DateTime.UtcNow;
        string[] formats = [
            "MMMM d", "MMMM dd", "MMM d", "MMM dd",
            "d MMMM", "dd MMMM", "d MMM", "dd MMM",
            "MMMM d, yyyy", "MMM d, yyyy",
            "yyyy-MM-dd", "MM/dd/yyyy", "dd/MM/yyyy"
        ];

        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(input, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                if (!format.Contains("yyyy"))
                {
                    date = new DateTime(now.Year, date.Month, date.Day);
                    if (date > now) date = date.AddYears(-1);
                }
                return (date.Date, date.Date.AddDays(1));
            }
        }

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

    [HttpGet("progress")]
    public IActionResult Progress()
    {
        return Ok(new
        {
            _progress.IsScanning,
            _progress.TotalFiles,
            _progress.AlreadyIndexed,
            _progress.ProcessedFiles,
            _progress.RemainingFiles,
            _progress.PercentComplete,
            _progress.CurrentFile
        });
    }

    [HttpPost("screenshots/{id}/reocr")]
    public async Task<IActionResult> ReOcr(int id)
    {
        var screenshot = await _db.Screenshots.FindAsync(id);
        if (screenshot == null) return NotFound();

        // Reset status to Pending and clear OCR text
        screenshot.Status = ScreenshotStatus.Pending;
        screenshot.OcrText = null;
        await _db.SaveChangesAsync();
        _progress.NotifyScreenshotStatusChanged(id, ScreenshotStatus.Pending);

        // Enqueue for processing via the priority queue
        await _watcher.EnqueueForProcessingAsync(screenshot.Path);

        return Accepted();
    }

    [HttpPost("screenshots/{id}/notes")]
    public async Task<IActionResult> UpdateNotes(int id, [FromBody] NotesRequest request)
    {
        var screenshot = await _db.Screenshots.FindAsync(id);
        if (screenshot == null) return NotFound();

        screenshot.Notes = request.Notes;
        await _db.SaveChangesAsync();

        return Ok();
    }

    [HttpDelete("screenshots/{id}")]
    public async Task<IActionResult> DeleteScreenshot(int id)
    {
        var screenshot = await _db.Screenshots.FindAsync(id);
        if (screenshot == null) return NotFound();

        // Delete file from disk first - if this fails, don't remove from DB
        if (System.IO.File.Exists(screenshot.Path))
        {
            System.IO.File.Delete(screenshot.Path);
        }

        // Find next screenshot (by date, descending)
        var nextScreenshot = await _db.Screenshots
            .Where(s => s.CreatedAt < screenshot.CreatedAt)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();

        // Remove from database
        _db.Screenshots.Remove(screenshot);
        await _db.SaveChangesAsync();

        return Ok(new { nextId = nextScreenshot?.Id });
    }
}
