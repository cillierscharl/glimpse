using System.Diagnostics;

namespace Glimpse.Services;

public class GpuStatsService
{
    private readonly ILogger<GpuStatsService> _logger;

    public GpuStatsService(ILogger<GpuStatsService> logger)
    {
        _logger = logger;
    }

    public async Task<GpuStats?> GetStatsAsync()
    {
        try
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
            if (process == null) return null;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0) return null;

            var parts = output.Trim().Split(',').Select(p => p.Trim()).ToArray();
            if (parts.Length < 7) return null;

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
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get GPU stats");
            return null;
        }
    }
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
