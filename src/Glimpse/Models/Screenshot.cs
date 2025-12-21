namespace Glimpse.Models;

public class Screenshot
{
    public int Id { get; set; }
    public required string Path { get; set; }
    public string? OcrText { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
