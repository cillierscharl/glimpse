using Glimpse.Data;
using Glimpse.Services;
using Microsoft.AspNetCore.Mvc;

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
            _progress.ProcessedFiles,
            _progress.RemainingFiles,
            _progress.PercentComplete,
            _progress.CurrentFile
        });
    }
}
