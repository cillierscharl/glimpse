namespace Glimpse.Models;

public class SearchResponse
{
    public string? Query { get; set; }
    public int TotalCount { get; set; }
    public int Limit { get; set; }
    public int Offset { get; set; }
    public List<ScreenshotResult> Results { get; set; } = [];
}

public class ScreenshotResult
{
    public int Id { get; set; }
    public string Filename { get; set; } = "";
    public string? OcrText { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? ImageBase64 { get; set; }
}

public record NotesRequest(string? Notes);
