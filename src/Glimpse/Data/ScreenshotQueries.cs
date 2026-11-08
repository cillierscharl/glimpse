using System.Globalization;
using Glimpse.Helpers;
using Glimpse.Models;
using Microsoft.EntityFrameworkCore;

namespace Glimpse.Data;

public static class ScreenshotQueries
{
    /// <summary>
    /// Applies the search box semantics shared by the UI and the API: a query that parses as a date
    /// ("Nov 26", "2024-11-26", "March") filters by that day or month; anything else is a substring
    /// match on the OCR text and notes. <paramref name="tag"/> additionally filters by tag name.
    /// </summary>
    public static IQueryable<Screenshot> Search(this IQueryable<Screenshot> query, string? q, string? tag)
    {
        if (!string.IsNullOrWhiteSpace(q))
        {
            if (DateHelper.TryParseDate(q) is (var start, var end))
            {
                query = query.Where(s => s.CreatedAt >= start && s.CreatedAt < end);
            }
            else
            {
                var pattern = $"%{q}%";
                query = query.Where(s =>
                    (s.OcrText != null && EF.Functions.Like(s.OcrText, pattern)) ||
                    (s.Notes != null && EF.Functions.Like(s.Notes, pattern)));
            }
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            var tagName = tag.Trim().ToLowerInvariant();
            query = query.Where(s => s.Tags.Any(t => t.Name == tagName));
        }

        return query;
    }

    /// <summary>Newest first, with Id as a tie-breaker so the order is total (required for keyset paging).</summary>
    public static IOrderedQueryable<Screenshot> NewestFirst(this IQueryable<Screenshot> query) =>
        query.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id);

    /// <summary>Screenshots that come after <paramref name="cursor"/> in <see cref="NewestFirst"/> order.</summary>
    public static IQueryable<Screenshot> After(this IQueryable<Screenshot> query, PageCursor cursor) =>
        query.Where(s => s.CreatedAt < cursor.CreatedAt || (s.CreatedAt == cursor.CreatedAt && s.Id < cursor.Id));
}

/// <summary>
/// Position of the last screenshot on a page. Keyset paging stays correct when screenshots are added or
/// deleted between page loads, unlike offset paging.
/// </summary>
public readonly record struct PageCursor(DateTime CreatedAt, int Id)
{
    public static PageCursor From(Screenshot s) => new(s.CreatedAt, s.Id);

    public override string ToString() => $"{CreatedAt.Ticks}_{Id}";

    public static bool TryParse(string? value, out PageCursor cursor)
    {
        cursor = default;
        var parts = value?.Split('_');
        if (parts is not [var ticksText, var idText]
            || !long.TryParse(ticksText, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || !int.TryParse(idText, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || ticks > DateTime.MaxValue.Ticks)
        {
            return false;
        }

        cursor = new PageCursor(new DateTime(ticks), id);
        return true;
    }
}
