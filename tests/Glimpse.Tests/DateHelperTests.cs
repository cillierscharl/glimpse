using Glimpse.Helpers;
using Xunit;

namespace Glimpse.Tests;

public class DateHelperTests
{
    [Theory]
    [InlineData("2024-11-26")]
    [InlineData("Nov 26, 2024")]
    [InlineData("November 26, 2024")]
    public void TryParseDate_ParsesFullDates(string input)
    {
        var result = DateHelper.TryParseDate(input);

        Assert.NotNull(result);
        Assert.Equal(new DateTime(2024, 11, 26), result.Value.start);
        Assert.Equal(new DateTime(2024, 11, 27), result.Value.end);
    }

    [Fact]
    public void TryParseDate_ParsesMonthOnly()
    {
        var result = DateHelper.TryParseDate("January");

        Assert.NotNull(result);
        Assert.Equal(1, result.Value.start.Month);
        Assert.Equal(1, result.Value.start.Day);
        Assert.Equal(2, result.Value.end.Month);
    }

    [Fact]
    public void TryParseDate_ParsesAbbreviatedMonth()
    {
        var result = DateHelper.TryParseDate("Jan");

        Assert.NotNull(result);
        Assert.Equal(1, result.Value.start.Month);
    }

    [Fact]
    public void TryParseDate_ReturnsNullForInvalidInput()
    {
        var result = DateHelper.TryParseDate("not a date");

        Assert.Null(result);
    }

    [Fact]
    public void TryParseDate_ReturnsNullForEmptyString()
    {
        var result = DateHelper.TryParseDate("");

        Assert.Null(result);
    }

    [Theory]
    [InlineData("Nov 26")]
    [InlineData("November 26")]
    [InlineData("26 Nov")]
    [InlineData("26 November")]
    public void TryParseDate_ParsesDayMonthWithoutYear(string input)
    {
        var result = DateHelper.TryParseDate(input);

        Assert.NotNull(result);
        Assert.Equal(11, result.Value.start.Month);
        Assert.Equal(26, result.Value.start.Day);
    }
}
