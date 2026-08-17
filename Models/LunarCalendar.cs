using System.Globalization;

namespace TransparentCalendar.Models;

/// <summary>一天的农历信息。<see cref="Label"/> 是日历格上要显示的那一行。</summary>
public sealed record LunarInfo(
    string MonthName,
    string DayName,
    string? Festival,
    string? SolarTerm)
{
    /// <summary>显示优先级：传统节日 &gt; 节气 &gt; 农历日（初一显示月名）。</summary>
    public string Label => Festival ?? SolarTerm ?? DayName;

    /// <summary>节日与节气值得高亮，普通农历日不必。</summary>
    public bool IsHighlighted => Festival is not null || SolarTerm is not null;
}

/// <summary>
/// 农历换算。基于 .NET 内置的 <see cref="ChineseLunisolarCalendar"/>，**完全离线、零依赖**。
/// 法定节假日与调休不在这里 —— 那没有算法可推，见 <c>Services/HolidayService</c>。
/// </summary>
public static class LunarCalendar
{
    private static readonly ChineseLunisolarCalendar Calendar = new();

    private static readonly string[] MonthNames =
        ["正", "二", "三", "四", "五", "六", "七", "八", "九", "十", "冬", "腊"];

    private static readonly string[] DayPrefixes = ["初", "十", "廿", "卅"];

    private static readonly string[] DayDigits =
        ["十", "一", "二", "三", "四", "五", "六", "七", "八", "九"];

    private static readonly string[] SolarTermNames =
    [
        "小寒", "大寒", "立春", "雨水", "惊蛰", "春分",
        "清明", "谷雨", "立夏", "小满", "芒种", "夏至",
        "小暑", "大暑", "立秋", "处暑", "白露", "秋分",
        "寒露", "霜降", "立冬", "小雪", "大雪", "冬至"
    ];

    // 寿星公式：day = int(Y * 0.2422 + C) - int((Y - 1) / 4)，Y 为年份后两位。
    // C 值分 20 / 21 世纪两套，**不能混用**（混用会让多数节气整体偏一天）。
    private const double TropicalYearFraction = 0.2422;

    private static readonly double[] CenturyC20 =
    [
        6.11, 20.84, 4.6295, 19.4599, 6.3826, 21.4155,
        5.59, 20.888, 6.318, 21.86, 6.5, 22.20,
        7.928, 23.65, 8.35, 23.95, 8.44, 23.822,
        9.098, 24.218, 8.218, 23.08, 7.9, 22.60
    ];

    private static readonly double[] CenturyC21 =
    [
        5.4055, 20.12, 3.87, 18.73, 5.63, 20.646,
        4.81, 20.1, 5.52, 21.04, 5.678, 21.37,
        7.108, 22.83, 7.5, 23.13, 7.646, 23.042,
        8.318, 23.438, 7.438, 22.36, 7.18, 21.94
    ];

    /// <summary>寿星公式的已知例外年份（(年份, 节气序号) → 修正天数）。</summary>
    private static readonly Dictionary<(int Year, int Term), int> TermCorrections = new()
    {
        [(1982, 0)] = 1,
        [(2019, 0)] = -1,
        [(2082, 1)] = 1,
        [(2026, 3)] = -1,
        [(2084, 4)] = 1,
        [(2084, 5)] = 1,
        [(1911, 7)] = 1,
        [(1911, 8)] = 1,
        [(2008, 9)] = 1,
        [(1902, 10)] = 1,
        [(1928, 11)] = 1,
        [(1925, 12)] = 1,
        [(2016, 12)] = 1,
        [(1922, 13)] = 1,
        [(2002, 14)] = 1,
        [(1927, 16)] = 1,
        [(1942, 17)] = 1,
        [(2088, 18)] = 1,
        [(2089, 19)] = 1,
        [(2089, 20)] = 1,
        [(1978, 21)] = 1,
        [(1954, 22)] = 1,
        [(1918, 23)] = -1,
        [(2021, 23)] = -1
    };

    public static LunarInfo GetInfo(DateTime date)
    {
        var (monthName, dayName, isFirstDay, lunarMonth, lunarDay, isLeap) = Convert(date);

        return new LunarInfo(
            monthName,
            isFirstDay ? monthName + "月" : dayName,
            GetFestival(date, lunarMonth, lunarDay, isLeap),
            GetSolarTerm(date));
    }

    private static (string MonthName, string DayName, bool IsFirstDay, int Month, int Day, bool IsLeap) Convert(
        DateTime date)
    {
        // ChineseLunisolarCalendar 的有效区间之外（1901-02-19 之前 / 2101 之后）直接退化，
        // 否则会抛 ArgumentOutOfRangeException。
        if (date < Calendar.MinSupportedDateTime || date > Calendar.MaxSupportedDateTime)
        {
            return (string.Empty, string.Empty, false, 0, 0, false);
        }

        var month = Calendar.GetMonth(date);
        var day = Calendar.GetDayOfMonth(date);
        var leapMonth = Calendar.GetLeapMonth(Calendar.GetYear(date));

        var isLeap = leapMonth > 0 && month == leapMonth;
        // 存在闰月时，闰月之后的月序号要减一才对应实际月份
        var actualMonth = leapMonth > 0 && month >= leapMonth ? month - 1 : month;

        var monthName = (isLeap ? "闰" : string.Empty) + MonthNames[actualMonth - 1];
        return (monthName, FormatDay(day), day == 1, actualMonth, day, isLeap);
    }

    private static string FormatDay(int day)
    {
        if (day == 10) return "初十";
        if (day == 20) return "二十";
        if (day == 30) return "三十";

        return DayPrefixes[day / 10] + DayDigits[day % 10];
    }

    private static string? GetFestival(DateTime date, int lunarMonth, int lunarDay, bool isLeap)
    {
        // 闰月不过节
        if (isLeap || lunarMonth == 0)
        {
            return null;
        }

        // 除夕 = 腊月最后一天，腊月可能是 29 或 30 天，必须靠"次日是初一"来判断
        if (lunarMonth == 12 && IsLastDayOfLunarMonth(date))
        {
            return "除夕";
        }

        return (lunarMonth, lunarDay) switch
        {
            (1, 1) => "春节",
            (1, 15) => "元宵",
            (2, 2) => "龙抬头",
            (5, 5) => "端午",
            (7, 7) => "七夕",
            (7, 15) => "中元",
            (8, 15) => "中秋",
            (9, 9) => "重阳",
            (12, 8) => "腊八",
            _ => null
        };
    }

    private static bool IsLastDayOfLunarMonth(DateTime date)
    {
        var next = date.AddDays(1);
        return next <= Calendar.MaxSupportedDateTime && Calendar.GetDayOfMonth(next) == 1;
    }

    /// <summary>
    /// 24 节气，用寿星公式近似（1900–2100 适用，误差在 1 日内的极端年份由修正项处理）。
    /// </summary>
    public static string? GetSolarTerm(DateTime date)
    {
        if (date.Year is < 1901 or > 2100)
        {
            return null;
        }

        for (var index = 0; index < 24; index++)
        {
            if (index / 2 + 1 != date.Month)
            {
                continue;
            }

            if (GetSolarTermDay(date.Year, index) == date.Day)
            {
                return SolarTermNames[index];
            }
        }

        return null;
    }

    private static int GetSolarTermDay(int year, int termIndex)
    {
        var y = year % 100;
        var c = year < 2000 ? CenturyC20[termIndex] : CenturyC21[termIndex];

        var day = (int)(y * TropicalYearFraction + c) - (y - 1) / 4;

        return day + (TermCorrections.TryGetValue((year, termIndex), out var correction) ? correction : 0);
    }
}
