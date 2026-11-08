using Glimpse.Helpers;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Glimpse.Tests;

public class ConfigurationExtensionsTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void UsesConfiguredPaths()
    {
        var config = Config(new() { ["Screenshots:WatchPath"] = "/shots", ["Database:Path"] = "/data/g.db" });

        Assert.Equal("/shots", config.GetWatchPath());
        Assert.Equal("/data/g.db", config.GetDatabasePath());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void FallsBackToDefaultsWhenUnsetOrEmpty(string? value)
    {
        var config = Config(new() { ["Screenshots:WatchPath"] = value, ["Database:Path"] = value });

        Assert.EndsWith(Path.Combine("Pictures", "Screenshots"), config.GetWatchPath());
        Assert.EndsWith(Path.Combine("glimpse", "glimpse.db"), config.GetDatabasePath());
    }
}
