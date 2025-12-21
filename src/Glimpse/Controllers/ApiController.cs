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

    public ApiController(GlimpseDbContext db, ScanProgressService progress)
    {
        _db = db;
        _progress = progress;
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

    [HttpDelete("screenshots/{id}")]
    public async Task<IActionResult> DeleteScreenshot(int id)
    {
        var screenshot = await _db.Screenshots.FindAsync(id);
        if (screenshot == null) return NotFound();

        // Find next screenshot (by date, descending)
        var nextScreenshot = await _db.Screenshots
            .Where(s => s.CreatedAt < screenshot.CreatedAt)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync();

        // Delete file from disk
        try
        {
            if (System.IO.File.Exists(screenshot.Path))
            {
                System.IO.File.Delete(screenshot.Path);
            }
        }
        catch { /* ignore file deletion errors */ }

        // Remove from database
        _db.Screenshots.Remove(screenshot);
        await _db.SaveChangesAsync();

        return Ok(new { nextId = nextScreenshot?.Id });
    }
}
