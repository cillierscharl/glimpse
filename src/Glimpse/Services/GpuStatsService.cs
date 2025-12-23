using System.Diagnostics;
using Polly;
using Polly.Retry;

namespace Glimpse.Services;

public class GpuStatsService
{
    private readonly ILogger<GpuStatsService> _logger;
    private readonly ResiliencePipeline<GpuStats?> _retryPipeline;

    public GpuStatsService(ILogger<GpuStatsService> logger)
    {
        _logger = logger;
        _retryPipeline = new ResiliencePipelineBuilder<GpuStats?>()
            .AddRetry(new RetryStrategyOptions<GpuStats?>
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromMilliseconds(100),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder<GpuStats?>()
                    .Handle<NvmlException>(),
                OnRetry = args =>
                {
                    logger.LogDebug("Retrying nvidia-smi (attempt {Attempt})", args.AttemptNumber + 1);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    public async Task<GpuStats?> GetStatsAsync()
    {
        try
        {
            return await _retryPipeline.ExecuteAsync(async _ => await QueryNvidiaSmiAsync());
        }
        catch (NvmlException ex)
        {
            _logger.LogWarning("nvidia-smi failed after retries: {Error}", ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get GPU stats");
            return null;
        }
    }

    private async Task<GpuStats?> QueryNvidiaSmiAsync()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "nvidia-smi",
            Arguments = "--query-gpu=name,temperature.gpu,power.draw,power.limit,memory.used,memory.total,utilization.gpu --format=csv,noheader,nounits",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            _logger.LogWarning("Failed to start nvidia-smi process");
            return null;
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new NvmlException($"Exit code {process.ExitCode}: {error.Trim()}");
        }

        var parts = output.Trim().Split(',').Select(p => p.Trim()).ToArray();
        if (parts.Length < 7)
        {
            _logger.LogWarning("nvidia-smi returned unexpected output ({PartCount} parts): {Output}", parts.Length, output.Trim());
            return null;
        }

        return new GpuStats(
            Name: parts[0],
            TemperatureC: int.TryParse(parts[1], out var temp) ? temp : 0,
            PowerDrawW: double.TryParse(parts[2], out var power) ? power : 0,
            PowerLimitW: double.TryParse(parts[3], out var limit) ? limit : 0,
            MemoryUsedMB: int.TryParse(parts[4], out var memUsed) ? memUsed : 0,
            MemoryTotalMB: int.TryParse(parts[5], out var memTotal) ? memTotal : 0,
            UtilizationPercent: int.TryParse(parts[6], out var util) ? util : 0
        );
    }
}

public class NvmlException : Exception
{
    public NvmlException(string message) : base(message) { }
}

public record GpuStats(
    string Name,
    int TemperatureC,
    double PowerDrawW,
    double PowerLimitW,
    int MemoryUsedMB,
    int MemoryTotalMB,
    int UtilizationPercent
);
