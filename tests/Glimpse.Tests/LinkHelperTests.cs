using Glimpse.Helpers;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;

namespace Glimpse.Tests;

public class LinkHelperTests
{
    private static Dictionary<string, string> ParseQuery(string url) =>
        QueryHelpers.ParseQuery(new Uri(new Uri("http://localhost"), url).Query)
            .ToDictionary(kv => kv.Key, kv => kv.Value.ToString());

    [Fact]
    public void Search_EncodesCharactersThatWouldBreakTheUrl()
    {
        var url = LinkHelper.Search("c# & .net?", "my tag");

        Assert.DoesNotContain("#", url);
        Assert.Equal(new Dictionary<string, string> { ["q"] = "c# & .net?", ["tag"] = "my tag" }, ParseQuery(url));
    }

    [Theory]
    [InlineData(null, null, "/")]
    [InlineData("", "", "/")]
    [InlineData("hello", null, "/?q=hello")]
    [InlineData(null, "work", "/?tag=work")]
    public void Search_OmitsEmptyParameters(string? q, string? tag, string expected)
    {
        Assert.Equal(expected, LinkHelper.Search(q, tag));
    }

    [Fact]
    public void Detail_KeepsSearchQuery()
    {
        Assert.Equal("/detail/5", LinkHelper.Detail(5));
        Assert.Equal("c#", ParseQuery(LinkHelper.Detail(5, "c#"))["q"]);
    }

    [Fact]
    public void Image_EncodesFilename()
    {
        Assert.Equal("/screenshots/50%25%20done%20%232.png", LinkHelper.Image("/screenshots/50% done #2.png"));
    }
}
