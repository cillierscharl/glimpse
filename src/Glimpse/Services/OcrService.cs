using System.Net.Http.Json;
using System.Text.Json;

namespace Glimpse.Services;

public class OcrService
{
    private static readonly TimeSpan PullRetryDelay = TimeSpan.FromSeconds(30);

    private readonly ILogger<OcrService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly string _baseUrl;

    public bool IsReady { get; private set; }
    public string? Status { get; private set; } = "Waiting for Ollama...";

    public event Action? OnStatusChange;

    public OcrService(IConfiguration config, ILogger<OcrService> logger)
    {
        _logger = logger;
        _baseUrl = config.GetValue<string>("Ollama:BaseUrl") ?? "http://localhost:11434";
        _model = config.GetValue<string>("Ollama:Model") ?? "minicpm-v";
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
    }

    public async Task WaitForReadyAsync(CancellationToken ct = default)
    {
        SetStatus("Waiting for Ollama...");
        while (!await IsReachableAsync(ct))
        {
            await Task.Delay(2000, ct);
        }
        _logger.LogInformation("Ollama is reachable");

        SetStatus($"Checking for {_model}...");
        while (!await CheckModelExistsAsync(ct))
        {
            SetStatus($"Pulling {_model} (this may take a few minutes)...");
            _logger.LogInformation("Pulling model {Model}...", _model);
            try
            {
                await PullModelAsync(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // e.g. no network on first start, or a typo in OLLAMA_MODEL. Keep the UI showing why.
                _logger.LogError(ex, "Failed to pull model {Model}", _model);
                SetStatus($"Failed to pull {_model}: {ex.Message} (retrying in {PullRetryDelay.TotalSeconds:0}s)");
                await Task.Delay(PullRetryDelay, ct);
            }
        }

        IsReady = true;
        SetStatus(null);
        _logger.LogInformation("OCR service ready with model {Model}", _model);
    }

    private async Task<bool> IsReachableAsync(CancellationToken ct)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"{_baseUrl}/api/tags", ct);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private async Task<bool> CheckModelExistsAsync(CancellationToken ct)
    {
        try
        {
            var json = await _httpClient.GetFromJsonAsync<JsonElement>($"{_baseUrl}/api/tags", ct);
            return json.TryGetProperty("models", out var models)
                && models.EnumerateArray().Any(m =>
                    m.TryGetProperty("name", out var name) && name.GetString()?.StartsWith(_model) == true);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return false;
        }
    }

    private async Task PullModelAsync(CancellationToken ct)
    {
        // ResponseHeadersRead so progress can be streamed; the default would buffer the whole response
        // (i.e. wait for the entire download) before we could read a single status line.
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/pull")
        {
            Content = JsonContent.Create(new { model = _model })
        };
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                throw new InvalidOperationException(error.GetString());
            }

            if (root.TryGetProperty("status", out var status))
            {
                var text = $"Pulling {_model}: {status.GetString()}";
                if (root.TryGetProperty("total", out var total) && total.GetInt64() > 0
                    && root.TryGetProperty("completed", out var completed))
                {
                    text += $" ({completed.GetInt64() * 100 / total.GetInt64()}%)";
                }

                // Ollama sends many progress lines per second; only notify clients when the text changes.
                if (text != Status) SetStatus(text);
            }
        }
    }

    /// <summary>
    /// Extracts text from an image. Throws if Ollama fails, so the caller can record the screenshot as
    /// Failed rather than as successfully processed with no text.
    /// </summary>
    public async Task<string> ExtractTextAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        if (!IsReady) throw new InvalidOperationException("OCR service is not ready");

        var imageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        var request = new
        {
            model = _model,
            prompt = "Extract all text visible in this image. Return only the extracted text, nothing else. If there is no text, return an empty response.",
            images = new[] { Convert.ToBase64String(imageBytes) },
            stream = false
        };

        using var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/generate", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Ollama returned {(int)response.StatusCode}: {error}");
        }

        var result = await response.Content.ReadFromJsonAsync<OllamaResponse>(cancellationToken);
        return result?.Response?.Trim() ?? string.Empty;
    }

    private void SetStatus(string? status)
    {
        Status = status;
        OnStatusChange?.Invoke();
    }

    private record OllamaResponse(string? Response);
}
