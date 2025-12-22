using System.Text.RegularExpressions;

namespace Glimpse.Helpers;

public static partial class MarkdownHelper
{
    /// <summary>
    /// Strips common markdown formatting from text for plain display.
    /// </summary>
    public static string StripMarkdown(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;

        var result = text;

        // Remove bold: **text** or __text__
        result = BoldDoubleAsterisk().Replace(result, "$1");
        result = BoldDoubleUnderscore().Replace(result, "$1");

        // Remove italic: *text* or _text_ (but not within words)
        result = ItalicAsterisk().Replace(result, "$1");
        result = ItalicUnderscore().Replace(result, " $1 ");

        // Remove headers: # Header
        result = Headers().Replace(result, "");

        // Remove images first (before links, as images start with !)
        // ![alt](url) -> alt
        result = Images().Replace(result, "$1");

        // Remove links: [text](url) -> text
        result = Links().Replace(result, "$1");

        // Remove inline code: `code`
        result = InlineCode().Replace(result, "$1");

        // Remove strikethrough: ~~text~~
        result = Strikethrough().Replace(result, "$1");

        // Clean up multiple spaces
        result = MultipleSpaces().Replace(result, " ");

        return result.Trim();
    }

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex BoldDoubleAsterisk();

    [GeneratedRegex(@"__(.+?)__")]
    private static partial Regex BoldDoubleUnderscore();

    [GeneratedRegex(@"(?<!\*)\*([^*]+)\*(?!\*)")]
    private static partial Regex ItalicAsterisk();

    [GeneratedRegex(@"(?<=\s|^)_([^_]+)_(?=\s|$)")]
    private static partial Regex ItalicUnderscore();

    [GeneratedRegex(@"^#{1,6}\s+", RegexOptions.Multiline)]
    private static partial Regex Headers();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)")]
    private static partial Regex Links();

    [GeneratedRegex(@"!\[([^\]]*)\]\([^)]+\)")]
    private static partial Regex Images();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex InlineCode();

    [GeneratedRegex(@"~~(.+?)~~")]
    private static partial Regex Strikethrough();

    [GeneratedRegex(@" {2,}")]
    private static partial Regex MultipleSpaces();
}
