using System.Diagnostics;

namespace Glimpse.Services;

public class GpuStatsService(ILogger<GpuStatsService> logger)
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(5);

    public async Task<GpuStats?> GetStatsAsync(CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await QueryNvidiaSmiAsync(ct);
            }
            catch (NvmlException ex) when (attempt < MaxAttempts)
            {
                // nvidia-smi intermittently fails with transient NVML errors; a quick retry usually succeeds.
                logger.LogDebug("nvidia-smi failed (attempt {Attempt}): {Error}", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), ct);
            }
            catch (NvmlException ex)
            {
                logger.LogWarning("nvidia-smi failed after {Attempts} attempts: {Error}", MaxAttempts, ex.Message);
                return null;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Failed to get GPU stats");
                return null;
            }
        }
    }

    private async Task<GpuStats?> QueryNvidiaSmiAsync(CancellationToken ct)
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
            logger.LogWarning("Failed to start nvidia-smi process");
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(QueryTimeout);

        // Read both streams concurrently so neither pipe can fill up and block the process.
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"nvidia-smi did not respond within {QueryTimeout.TotalSeconds}s");
        }

        var output = await stdout;
        var error = await stderr;

        if (process.ExitCode != 0)
        {
            throw new NvmlException($"Exit code {process.ExitCode}: {error.Trim()}");
        }

        var parts = output.Trim().Split(',').Select(p => p.Trim()).ToArray();
        if (parts.Length < 7)
        {
            logger.LogWarning("nvidia-smi returned unexpected output ({PartCount} parts): {Output}", parts.Length, output.Trim());
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

public class NvmlException(string message) : Exception(message);

public record GpuStats(
    string Name,
    int TemperatureC,
    double PowerDrawW,
    double PowerLimitW,
    int MemoryUsedMB,
    int MemoryTotalMB,
    int UtilizationPercent
);
