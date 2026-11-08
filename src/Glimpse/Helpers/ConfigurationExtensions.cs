namespace Glimpse.Helpers;

public static class ConfigurationExtensions
{
    // Empty strings count as "not set" so that e.g. `Screenshots__WatchPath=` falls back to the default
    // instead of resolving to the current directory (or throwing).

    public static string GetWatchPath(this IConfiguration config) =>
        NonEmpty(config["Screenshots:WatchPath"])
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures", "Screenshots");

    public static string GetDatabasePath(this IConfiguration config) =>
        NonEmpty(config["Database:Path"])
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "glimpse", "glimpse.db");

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
