using System.Globalization;

namespace Glimpse.Helpers;

public static class DateHelper
{
    private static readonly string[] DateFormats = [
        "MMMM d", "MMMM dd", "MMM d", "MMM dd",  // November 26, Nov 26
        "d MMMM", "dd MMMM", "d MMM", "dd MMM",  // 26 November, 26 Nov
        "MMMM d, yyyy", "MMM d, yyyy",           // November 26, 2024
        "yyyy-MM-dd", "MM/dd/yyyy", "dd/MM/yyyy" // 2024-11-26
    ];

    /// <summary>
    /// Attempts to parse a date range from natural language input.
    /// Returns a tuple of (start, end) dates for filtering.
    /// </summary>
    public static (DateTime start, DateTime end)? TryParseDate(string input)
    {
        var now = DateTime.UtcNow;

        // Try common date patterns
        foreach (var format in DateFormats)
        {
            if (DateTime.TryParseExact(input, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                // If no year specified, try current year first, then previous year
                if (!format.Contains("yyyy"))
                {
                    date = new DateTime(now.Year, date.Month, date.Day);
                    if (date > now) date = date.AddYears(-1);
                }
                return (date.Date, date.Date.AddDays(1));
            }
        }

        // Try month name only (e.g., "November")
        for (int m = 1; m <= 12; m++)
        {
            var monthName = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(m);
            var shortMonthName = CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(m);
            if (input.Equals(monthName, StringComparison.OrdinalIgnoreCase) ||
                input.Equals(shortMonthName, StringComparison.OrdinalIgnoreCase))
            {
                var year = now.Month >= m ? now.Year : now.Year - 1;
                var start = new DateTime(year, m, 1);
                return (start, start.AddMonths(1));
            }
        }

        return null;
    }
}
