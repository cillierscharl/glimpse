using System.Net.Http.Json;
using System.Text.Json;

namespace Glimpse.Services;

public class OcrService
{
    private readonly ILogger<OcrService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly string _baseUrl;

    public OcrService(IConfiguration config, ILogger<OcrService> logger)
    {
        _logger = logger;
        _baseUrl = config.GetValue<string>("Ollama:BaseUrl") ?? "http://localhost:11434";
        _model = config.GetValue<string>("Ollama:Model") ?? "llama3.2-vision";
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    public async Task<string> ExtractTextAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        try
        {
            // Read and encode image as base64
            var imageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
            var base64Image = Convert.ToBase64String(imageBytes);

            var request = new
            {
                model = _model,
                prompt = "Extract all text visible in this image. Return only the extracted text, nothing else. If there is no text, return an empty response.",
                images = new[] { base64Image },
                stream = false
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"{_baseUrl}/api/generate",
                request,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Ollama API error: {Status} - {Error}", response.StatusCode, error);
                return string.Empty;
            }

            var result = await response.Content.ReadFromJsonAsync<OllamaResponse>(cancellationToken);
            return result?.Response?.Trim() ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to OCR image: {Path}", imagePath);
            return string.Empty;
        }
    }

    private record OllamaResponse(string Response);
}
