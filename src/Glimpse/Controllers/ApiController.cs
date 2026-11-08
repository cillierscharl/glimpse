using Glimpse.Data;
using Glimpse.Helpers;
using Glimpse.Models;
using Glimpse.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Glimpse.Controllers;

[ApiController]
[Route("api")]
[Produces("application/json")]
public class ApiController(GlimpseDbContext db, ScanProgressService progress, ScreenshotWatcherService watcher)
    : ControllerBase
{
    /// <summary>
    /// Search screenshots by text content or date
    /// </summary>
    /// <param name="q">Search query (text to find in OCR/notes, or date like "Nov 26", "2024-11-26")</param>
    /// <param name="tag">Filter by tag name</param>
    /// <param name="includeImages">Include base64-encoded images in response (default: false)</param>
    /// <param name="limit">Maximum number of results (default: 24, max: 100)</param>
    /// <param name="offset">Number of results to skip for pagination (default: 0)</param>
    [HttpGet("screenshots/search")]
    public async Task<ActionResult<SearchResponse>> Search(
        [FromQuery] string? q = null,
        [FromQuery] string? tag = null,
        [FromQuery] bool includeImages = false,
        [FromQuery] int limit = 24,
        [FromQuery] int offset = 0)
    {
        limit = Math.Clamp(limit, 1, 100);
        offset = Math.Max(0, offset);

        var query = db.Screenshots.Search(q, tag);

        var totalCount = await query.CountAsync();
        var screenshots = await query
            .NewestFirst()
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        var results = new List<ScreenshotResult>();
        foreach (var s in screenshots)
        {
            results.Add(await ToResultAsync(s, includeImages));
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
        var s = await db.Screenshots.FindAsync(id);
        if (s == null) return NotFound();

        return Ok(await ToResultAsync(s, includeImage));
    }

    [HttpGet("progress")]
    public ActionResult<ProgressUpdate> Progress() => progress.GetSnapshot();

    [HttpPost("screenshots/{id}/reocr")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReOcr(int id)
    {
        var screenshot = await db.Screenshots.FindAsync(id);
        if (screenshot == null) return NotFound();

        // Reset status to Pending and clear OCR text
        screenshot.Status = ScreenshotStatus.Pending;
        screenshot.OcrText = null;
        await db.SaveChangesAsync();
        progress.NotifyScreenshotStatusChanged(id, ScreenshotStatus.Pending);

        // Enqueue for processing via the priority queue
        await watcher.EnqueueForProcessingAsync(screenshot.Path);

        return Accepted();
    }

    [HttpPost("screenshots/{id}/notes")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateNotes(int id, [FromBody] NotesRequest request)
    {
        var screenshot = await db.Screenshots.FindAsync(id);
        if (screenshot == null) return NotFound();

        screenshot.Notes = request.Notes;
        await db.SaveChangesAsync();

        return Ok();
    }

    [HttpDelete("screenshots/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteScreenshot(int id)
    {
        var screenshot = await db.Screenshots.FindAsync(id);
        if (screenshot == null) return NotFound();

        // Delete file from disk first - if this fails, don't remove from DB
        if (System.IO.File.Exists(screenshot.Path))
        {
            System.IO.File.Delete(screenshot.Path);
        }

        // The screenshot to show next: the one after this in the grid's (newest-first) order
        var nextId = await db.Screenshots
            .After(PageCursor.From(screenshot))
            .NewestFirst()
            .Select(s => (int?)s.Id)
            .FirstOrDefaultAsync();

        db.Screenshots.Remove(screenshot);
        await db.SaveChangesAsync();

        return Ok(new { nextId });
    }

    // ===== Tag Endpoints =====

    /// <summary>
    /// Get all tags
    /// </summary>
    [HttpGet("tags")]
    public async Task<ActionResult<List<TagDto>>> GetTags()
    {
        var tags = await db.Tags
            .OrderBy(t => t.Name)
            .Select(t => new TagDto { Id = t.Id, Name = t.Name })
            .ToListAsync();
        return Ok(tags);
    }

    /// <summary>
    /// Get tags for a specific screenshot
    /// </summary>
    [HttpGet("screenshots/{id}/tags")]
    public async Task<ActionResult<List<TagDto>>> GetScreenshotTags(int id)
    {
        var screenshot = await db.Screenshots
            .Include(s => s.Tags)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (screenshot == null) return NotFound();

        var tags = screenshot.Tags
            .Select(t => new TagDto { Id = t.Id, Name = t.Name })
            .OrderBy(t => t.Name)
            .ToList();
        return Ok(tags);
    }

    /// <summary>
    /// Add a tag to a screenshot (creates tag if it doesn't exist)
    /// </summary>
    [HttpPost("screenshots/{id}/tags")]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<TagDto>> AddTagToScreenshot(int id, [FromBody] AddTagRequest request)
    {
        var screenshot = await db.Screenshots
            .Include(s => s.Tags)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (screenshot == null) return NotFound();

        var tagName = request.Name.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(tagName)) return BadRequest("Tag name required");

        // Find or create tag
        var tag = await db.Tags.FirstOrDefaultAsync(t => t.Name == tagName);
        if (tag == null)
        {
            tag = new Tag { Name = tagName };
            db.Tags.Add(tag);
        }

        // Add if not already tagged
        if (!screenshot.Tags.Any(t => t.Id == tag.Id))
        {
            screenshot.Tags.Add(tag);
            await db.SaveChangesAsync();
        }

        return Ok(new TagDto { Id = tag.Id, Name = tag.Name });
    }

    /// <summary>
    /// Remove a tag from a screenshot
    /// </summary>
    [HttpDelete("screenshots/{id}/tags/{tagId}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveTagFromScreenshot(int id, int tagId)
    {
        var screenshot = await db.Screenshots
            .Include(s => s.Tags)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (screenshot == null) return NotFound();

        var tag = screenshot.Tags.FirstOrDefault(t => t.Id == tagId);
        if (tag != null)
        {
            screenshot.Tags.Remove(tag);
            await db.SaveChangesAsync();

            // Clean up orphaned tag
            var stillUsed = await db.Screenshots.AnyAsync(s => s.Tags.Any(t => t.Id == tagId));
            if (!stillUsed)
            {
                db.Tags.Remove(tag);
                await db.SaveChangesAsync();
            }
        }

        return NoContent();
    }

    private static async Task<ScreenshotResult> ToResultAsync(Screenshot s, bool includeImage) => new()
    {
        Id = s.Id,
        Filename = Path.GetFileName(s.Path),
        OcrText = s.OcrText,
        Notes = s.Notes,
        CreatedAt = s.CreatedAt,
        Width = s.Width,
        Height = s.Height,
        ImageBase64 = includeImage ? await FileHelper.GetBase64DataUrlAsync(s.Path) : null
    };
}
