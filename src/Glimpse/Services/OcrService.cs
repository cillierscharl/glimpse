using System.Net.Http.Json;
using System.Text.Json;

namespace Glimpse.Services;

public class OcrService
{
    private readonly ILogger<OcrService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly string _baseUrl;

    public bool IsReady { get; private set; }
    public string? Status { get; private set; }

    public event Action? OnStatusChange;

    public OcrService(IConfiguration config, ILogger<OcrService> logger)
    {
        _logger = logger;
        _baseUrl = config.GetValue<string>("Ollama:BaseUrl") ?? "http://localhost:11434";
        _model = config.GetValue<string>("Ollama:Model") ?? "minicpm-v";
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        Status = "Waiting for Ollama...";
    }

    public async Task WaitForReadyAsync(CancellationToken ct = default)
    {
        // Wait for Ollama to be reachable
        Status = "Waiting for Ollama...";
        OnStatusChange?.Invoke();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/api/tags", ct);
                if (response.IsSuccessStatusCode) break;
            }
            catch
            {
                // Not ready yet
            }
            await Task.Delay(2000, ct);
        }

        _logger.LogInformation("Ollama is reachable");

        // Check if model is available
        Status = $"Checking for {_model}...";
        OnStatusChange?.Invoke();

        var hasModel = await CheckModelExistsAsync(ct);
        if (!hasModel)
        {
            Status = $"Pulling {_model} (this may take a few minutes)...";
            OnStatusChange?.Invoke();
            _logger.LogInformation("Pulling model {Model}...", _model);

            await PullModelAsync(ct);
        }

        Status = null;
        IsReady = true;
        OnStatusChange?.Invoke();
        _logger.LogInformation("OCR service ready with model {Model}", _model);
    }

    private async Task<bool> CheckModelExistsAsync(CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/tags", ct);
            if (!response.IsSuccessStatusCode) return false;

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (json.TryGetProperty("models", out var models))
            {
                foreach (var model in models.EnumerateArray())
                {
                    if (model.TryGetProperty("name", out var name) &&
                        name.GetString()?.StartsWith(_model) == true)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task PullModelAsync(CancellationToken ct)
    {
        var request = new { name = _model };
        var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/pull", request, ct);

        // Stream the response to wait for completion
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        string? line;
        while (!ct.IsCancellationRequested && (line = await reader.ReadLineAsync(ct)) != null)
        {
            try
            {
                var json = JsonSerializer.Deserialize<JsonElement>(line);
                if (json.TryGetProperty("status", out var status))
                {
                    Status = $"Pulling {_model}: {status.GetString()}";
                    OnStatusChange?.Invoke();
                }
            }
            catch { }
        }
    }

    public async Task<string> ExtractTextAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        if (!IsReady) return string.Empty;

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
