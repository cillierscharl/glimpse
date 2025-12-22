namespace Glimpse.Models;

public enum ScreenshotStatus
{
    Pending,     // File detected, waiting for OCR
    Processing,  // OCR in progress
    Completed,   // OCR finished
    Failed       // OCR failed
}

public class Screenshot
{
    public int Id { get; set; }
    public required string Path { get; set; }
    public string? OcrText { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ScreenshotStatus Status { get; set; } = ScreenshotStatus.Pending;
}
