namespace TransparentCalendar.Models;

/// <summary>
/// 界面字号相对基准字号（`AppSettings.FontSize`，即月历日期的字号）的比例。
/// 与 `MainWindow.ScaledFont(ratio, minimum)` 配合使用。
/// </summary>
public static class FontScale
{
    /// <summary>月份标题。</summary>
    public const double MonthTitle = 0.95;

    /// <summary>日期格里的农历/节气副标题。原先是 0.32，实测过小。</summary>
    public const double Almanac = 0.42;

    /// <summary>
    /// 今日块左侧的大号日号。比日期格的数字大一截 —— 「今天」的识别任务在这里，
    /// 格子里只留字重那一声。
    /// </summary>
    public const double TodayNumber = 1.9;

    /// <summary>今日块里的日期行与农历行。</summary>
    public const double TodayMeta = 0.42;

    /// <summary>分区标题（列表里的"未完成待办"等）。</summary>
    public const double SectionTitle = 0.52;

    /// <summary>卡片标题（列表项日期、笔记标题）。</summary>
    public const double CardTitle = 0.48;

    /// <summary>正文（周头、今日待办、空状态）。</summary>
    public const double Body = 0.45;

    /// <summary>次要说明（列表项详情、笔记预览）。</summary>
    public const double Detail = 0.40;

    /// <summary>提示文字（空状态说明）。</summary>
    public const double Hint = 0.42;

    /// <summary>脚注（笔记的 URL 行）。</summary>
    public const double Footnote = 0.35;
}
