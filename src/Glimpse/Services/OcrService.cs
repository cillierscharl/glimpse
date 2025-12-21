using System.Diagnostics;

namespace Glimpse.Services;

public class OcrService
{
    private readonly ILogger<OcrService> _logger;
    private readonly string _language;

    public OcrService(IConfiguration config, ILogger<OcrService> logger)
    {
        _logger = logger;
        _language = config.GetValue<string>("Tesseract:Language") ?? "eng";
    }

    public async Task<string> ExtractTextAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "tesseract",
                // PSM 3 = Fully automatic page segmentation (default)
                // OEM 1 = LSTM neural net only (most accurate)
                Arguments = $"\"{imagePath}\" stdout -l {_language} --psm 3 --oem 1",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi);
            if (process == null) return string.Empty;
            
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            
            return output.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to OCR image: {Path}", imagePath);
            return string.Empty;
        }
    }
}
