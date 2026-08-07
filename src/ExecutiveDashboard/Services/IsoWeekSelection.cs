using System.Globalization;

namespace ExecutiveDashboard.Services;

internal static class IsoWeekSelection
{
    public static string? NormalizeWeekRouteValue(string? week, DateTimeOffset timestampUtc)
    {
        if (string.IsNullOrWhiteSpace(week))
        {
            return null;
        }

        var lastCompletedWeekStart = LastCompletedWeekStart(timestampUtc);
        var selectedWeekStart = ResolveSelectedWeekStart(week, lastCompletedWeekStart);
        return ToWeekPickerValue(selectedWeekStart);
    }

    public static DateTimeOffset ResolveSelectedWeekStart(string? week, DateTimeOffset lastCompletedWeekStart)
    {
        if (!TryParseWeekPickerValue(week, out var selectedWeekStart))
        {
            return lastCompletedWeekStart;
        }

        return selectedWeekStart > lastCompletedWeekStart
            ? lastCompletedWeekStart
            : selectedWeekStart;
    }

    public static bool TryParseWeekPickerValue(string? value, out DateTimeOffset weekStartUtc)
    {
        weekStartUtc = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var segments = value.Split("-W", StringSplitOptions.TrimEntries);
        if (segments.Length != 2
            || !int.TryParse(segments[0], out var year)
            || !int.TryParse(segments[1], out var week)
            || year is < 1 or > 9999)
        {
            return false;
        }

        if (week < 1 || week > ISOWeek.GetWeeksInYear(year))
        {
            return false;
        }

        var monday = ISOWeek.ToDateTime(year, week, DayOfWeek.Monday);
        weekStartUtc = new DateTimeOffset(monday, TimeSpan.Zero);
        return true;
    }

    public static string ToWeekPickerValue(DateTimeOffset weekStartUtc)
    {
        var utc = weekStartUtc.ToUniversalTime();
        var isoYear = ISOWeek.GetYear(utc.DateTime);
        var isoWeek = ISOWeek.GetWeekOfYear(utc.DateTime);
        return $"{isoYear:0000}-W{isoWeek:00}";
    }

    public static DateTimeOffset StartOfWeek(DateTimeOffset timestampUtc)
    {
        var utcTimestamp = timestampUtc.ToUniversalTime();
        var daysSinceMonday = ((int)utcTimestamp.DayOfWeek + 6) % 7;
        var startOfDay = new DateTimeOffset(utcTimestamp.Year, utcTimestamp.Month, utcTimestamp.Day, 0, 0, 0, TimeSpan.Zero);
        return startOfDay.AddDays(-daysSinceMonday);
    }

    public static DateTimeOffset LastCompletedWeekStart(DateTimeOffset timestampUtc) =>
        StartOfWeek(timestampUtc).AddDays(-7);
}
