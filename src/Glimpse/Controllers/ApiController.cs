using Glimpse.Data;
using Glimpse.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Glimpse.Controllers;

[ApiController]
[Route("api")]
public class ApiController : ControllerBase
{
    private readonly GlimpseDbContext _db;
    private readonly ScanProgressService _progress;
    private readonly OcrService _ocr;

    public ApiController(GlimpseDbContext db, ScanProgressService progress, OcrService ocr)
    {
        _db = db;
        _progress = progress;
        _ocr = ocr;
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

        var ocrText = await _ocr.ExtractTextAsync(screenshot.Path);
        screenshot.OcrText = ocrText;
        await _db.SaveChangesAsync();

        return Ok(new { ocrText });
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

public record NotesRequest(string? Notes);
