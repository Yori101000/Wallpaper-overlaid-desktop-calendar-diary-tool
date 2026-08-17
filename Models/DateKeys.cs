using System.Globalization;

namespace TransparentCalendar.Models;

/// <summary>
/// 日历条目的字典键格式。`calendar-data.json` 的键就是这个格式，
/// 必须始终用 InvariantCulture，别在别处自己拼日期字符串。
/// </summary>
public static class DateKeys
{
    public const string Format = "yyyy-MM-dd";

    public static string DateKey(DateTime date)
    {
        return date.ToString(Format, CultureInfo.InvariantCulture);
    }

    public static DateTime? ParseDateKey(string key)
    {
        return DateTime.TryParseExact(
            key,
            Format,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date
            : null;
    }
}
