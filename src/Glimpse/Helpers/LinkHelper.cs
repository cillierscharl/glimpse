using Microsoft.AspNetCore.WebUtilities;

namespace Glimpse.Helpers;

/// <summary>
/// Builds URLs for the views. Search text, tag names and filenames are user-controlled, so they must be
/// URL-encoded (Razor only HTML-encodes): otherwise a search for "c#" or a file named "50%.png" breaks the link.
/// </summary>
public static class LinkHelper
{
    public static string Image(string path) => "/screenshots/" + Uri.EscapeDataString(Path.GetFileName(path));

    public static string Search(string? q = null, string? tag = null) => WithQuery("/", q, tag);

    public static string Detail(int id, string? q = null) => WithQuery($"/detail/{id}", q, tag: null);

    private static string WithQuery(string path, string? q, string? tag)
    {
        var query = new Dictionary<string, string?>();
        if (!string.IsNullOrEmpty(tag)) query["tag"] = tag;
        if (!string.IsNullOrEmpty(q)) query["q"] = q;
        return QueryHelpers.AddQueryString(path, query);
    }
}
