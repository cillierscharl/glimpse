using Glimpse.Helpers;
using Xunit;

namespace Glimpse.Tests;

public class MarkdownHelperTests
{
    [Fact]
    public void StripMarkdown_RemovesBold()
    {
        var result = MarkdownHelper.StripMarkdown("This is **bold** text");
        Assert.Equal("This is bold text", result);
    }

    [Fact]
    public void StripMarkdown_RemovesDoubleBold()
    {
        var result = MarkdownHelper.StripMarkdown("This is __also bold__ text");
        Assert.Equal("This is also bold text", result);
    }

    [Fact]
    public void StripMarkdown_RemovesItalic()
    {
        var result = MarkdownHelper.StripMarkdown("This is *italic* text");
        Assert.Equal("This is italic text", result);
    }

    [Fact]
    public void StripMarkdown_RemovesHeaders()
    {
        var result = MarkdownHelper.StripMarkdown("# Header\nSome text");
        Assert.Equal("Header\nSome text", result);
    }

    [Fact]
    public void StripMarkdown_RemovesMultipleLevelHeaders()
    {
        var result = MarkdownHelper.StripMarkdown("## Level 2\n### Level 3");
        Assert.Equal("Level 2\nLevel 3", result);
    }

    [Fact]
    public void StripMarkdown_RemovesLinks()
    {
        var result = MarkdownHelper.StripMarkdown("Check [this link](https://example.com) out");
        Assert.Equal("Check this link out", result);
    }

    [Fact]
    public void StripMarkdown_RemovesImages()
    {
        var result = MarkdownHelper.StripMarkdown("Here is ![an image](https://example.com/img.png)");
        Assert.Equal("Here is an image", result);
    }

    [Fact]
    public void StripMarkdown_RemovesInlineCode()
    {
        var result = MarkdownHelper.StripMarkdown("Use the `console.log` function");
        Assert.Equal("Use the console.log function", result);
    }

    [Fact]
    public void StripMarkdown_RemovesStrikethrough()
    {
        var result = MarkdownHelper.StripMarkdown("This is ~~deleted~~ text");
        Assert.Equal("This is deleted text", result);
    }

    [Fact]
    public void StripMarkdown_HandlesNull()
    {
        var result = MarkdownHelper.StripMarkdown(null);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void StripMarkdown_HandlesEmptyString()
    {
        var result = MarkdownHelper.StripMarkdown("");
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void StripMarkdown_HandlesPlainText()
    {
        var result = MarkdownHelper.StripMarkdown("Just plain text");
        Assert.Equal("Just plain text", result);
    }

    [Fact]
    public void StripMarkdown_HandlesMixedFormatting()
    {
        var result = MarkdownHelper.StripMarkdown("**Title:** This is *important* with a [link](url)");
        Assert.Equal("Title: This is important with a link", result);
    }

    [Fact]
    public void StripMarkdown_CollapsesMultipleSpaces()
    {
        var result = MarkdownHelper.StripMarkdown("Too   many    spaces");
        Assert.Equal("Too many spaces", result);
    }
}
