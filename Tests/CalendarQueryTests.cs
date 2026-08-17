using TransparentCalendar.Models;
using Xunit;
using static TransparentCalendar.Models.CalendarQuery;
using static TransparentCalendar.Models.DateKeys;

namespace TransparentCalendar.Tests;

public class DateKeysTests
{
    [Fact]
    public void DateKey_始终用不变文化输出()
    {
        Assert.Equal("2026-08-14", DateKey(new DateTime(2026, 8, 14)));
        Assert.Equal("2026-01-05", DateKey(new DateTime(2026, 1, 5)));
    }

    [Fact]
    public void DateKey_与_ParseDateKey_互为逆运算()
    {
        var date = new DateTime(2026, 2, 29 - 1);
        Assert.Equal(date, ParseDateKey(DateKey(date)));
    }

    [Theory]
    [InlineData("2026-8-14")]
    [InlineData("2026/08/14")]
    [InlineData("14-08-2026")]
    [InlineData("")]
    [InlineData("垃圾")]
    public void ParseDateKey_拒绝非标准格式(string key)
    {
        Assert.Null(ParseDateKey(key));
    }
}

public class CalendarQueryTests
{
    private static TodoItem Todo(string text, bool done = false, string priority = "普通") =>
        new() { Text = text, IsDone = done, Priority = priority };

    [Fact]
    public void IsImportantTodo_按序数比较重要()
    {
        Assert.True(IsImportantTodo(Todo("x", priority: "重要")));
        Assert.False(IsImportantTodo(Todo("x", priority: "普通")));
        Assert.False(IsImportantTodo(Todo("x", priority: "")));
    }

    [Fact]
    public void EntryHasContent_空白待办与空日记视为无内容()
    {
        var empty = new CalendarEntry { Todos = [Todo("   ")], Diary = "  " };
        Assert.False(EntryHasContent(empty));

        Assert.True(EntryHasContent(new CalendarEntry { Diary = "有日记" }));
        Assert.True(EntryHasContent(new CalendarEntry { Todos = [Todo("有待办")] }));
    }

    [Fact]
    public void EntryHasContent_是空条目清理的判据()
    {
        // 用户点开日期又取消 —— 这种条目必须被判为无内容，否则数据文件会持续膨胀
        Assert.False(EntryHasContent(new CalendarEntry { Date = "2026-08-14" }));
    }

    [Fact]
    public void 搜索为空时匹配一切()
    {
        var entry = new CalendarEntry { Diary = "随便" };
        Assert.True(EntryMatchesSearch(entry, ""));
        Assert.True(EntryMatchesSearch(entry, "   "));
        Assert.True(TodoMatchesSearch(Todo("随便"), ""));
    }

    [Fact]
    public void 搜索命中日记与待办且忽略大小写()
    {
        var entry = new CalendarEntry { Diary = "Hello World", Todos = [Todo("买牛奶")] };

        Assert.True(EntryMatchesSearch(entry, "hello"));
        Assert.True(EntryMatchesSearch(entry, "牛奶"));
        Assert.False(EntryMatchesSearch(entry, "不存在的词"));
    }

    [Fact]
    public void 搜索也能命中优先级()
    {
        Assert.True(TodoMatchesSearch(Todo("x", priority: "重要"), "重要"));
    }

    [Fact]
    public void PreviewText_折叠换行并截断()
    {
        var text = "第一行\r\n第二行\n第三行";
        Assert.Equal("第一行 第二行 第三行", PreviewText(text));

        var longText = new string('字', 60);
        var preview = PreviewText(longText);
        Assert.EndsWith("...", preview, StringComparison.Ordinal);
        Assert.Equal(45, preview.Length);
    }

    [Fact]
    public void PreviewText_恰好到边界时不截断()
    {
        var text = new string('字', 42);
        Assert.Equal(text, PreviewText(text));
    }

    [Fact]
    public void BuildEntrySummary_区分待办数与未完成数()
    {
        var entry = new CalendarEntry
        {
            Todos = [Todo("a"), Todo("b", done: true), Todo("   ")],
            Diary = "日记内容"
        };

        var summary = BuildEntrySummary(entry);

        Assert.Contains("待办 2 项", summary, StringComparison.Ordinal);
        Assert.Contains("未完成 1 项", summary, StringComparison.Ordinal);
        Assert.Contains("日记内容", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildEntrySummary_全空时给出无内容()
    {
        Assert.Equal("无内容", BuildEntrySummary(new CalendarEntry()));
    }

    [Fact]
    public void CalculatePostponedDays_相对最初日期累计()
    {
        // 从 8-01 起，推迟到 8-05 应当是 4 天，而不是相对上一次的 1 天
        Assert.Equal(4, CalculatePostponedDays("2026-08-01", new DateTime(2026, 8, 5)));
        Assert.Equal(1, CalculatePostponedDays("2026-08-01", new DateTime(2026, 8, 2)));
    }

    [Fact]
    public void CalculatePostponedDays_至少为一天()
    {
        // 目标日期早于起始日期属于异常数据，也不能得出 0 或负数
        Assert.Equal(1, CalculatePostponedDays("2026-08-10", new DateTime(2026, 8, 1)));
    }

    [Fact]
    public void CalculatePostponedDays_起始日期非法时退化为一天()
    {
        Assert.Equal(1, CalculatePostponedDays("垃圾", new DateTime(2026, 8, 5)));
        Assert.Equal(1, CalculatePostponedDays("", new DateTime(2026, 8, 5)));
    }

    [Fact]
    public void TodoItem_Clone_产出独立副本并补齐默认优先级()
    {
        var original = new TodoItem { Text = "原始", Priority = "", PostponedDays = 3 };

        var copy = original.Clone();
        copy.Text = "改过";

        Assert.Equal("原始", original.Text);
        Assert.Equal("普通", copy.Priority);
        Assert.Equal(3, copy.PostponedDays);
    }

    [Fact]
    public void PostponedLabel_只在推迟过时出现()
    {
        Assert.Equal(string.Empty, new TodoItem().PostponedLabel);
        Assert.Equal("已推迟 3 天", new TodoItem { PostponedDays = 3 }.PostponedLabel);
    }

    private static readonly DateTime Today = new(2026, 8, 14);

    [Theory]
    [InlineData(-10, TodoUrgency.Overdue)]
    [InlineData(-1, TodoUrgency.Overdue)]
    [InlineData(0, TodoUrgency.Today)]
    [InlineData(1, TodoUrgency.Upcoming)]
    [InlineData(30, TodoUrgency.Upcoming)]
    public void ClassifyUrgency_按日期分组(int dayOffset, TodoUrgency expected)
    {
        Assert.Equal(expected, ClassifyUrgency(Today.AddDays(dayOffset), Today));
    }

    [Fact]
    public void OverdueDays_只对过去的日期为正()
    {
        Assert.Equal(5, OverdueDays(Today.AddDays(-5), Today));
        Assert.Equal(0, OverdueDays(Today, Today));
        Assert.Equal(0, OverdueDays(Today.AddDays(3), Today));
    }

    [Fact]
    public void GroupUnfinished_今天的待办不会被大量逾期项挤掉()
    {
        // 这是第 35 项修复的核心：早先全局 OrderBy(Date).Take(30) 会让 100 条远古遗留项
        // 把今天的待办完全挤出可视范围。
        var items = new List<(DateTime, TodoItem)>();
        for (var i = 1; i <= 100; i++)
        {
            items.Add((Today.AddDays(-i), Todo($"远古待办 {i}")));
        }

        items.Add((Today, Todo("今天要做的事")));

        var grouped = GroupUnfinished(items, Today);

        Assert.Equal(100, grouped[TodoUrgency.Overdue].Count);
        Assert.Single(grouped[TodoUrgency.Today]);
        Assert.Equal("今天要做的事", grouped[TodoUrgency.Today][0].Todo.Text);
        Assert.Empty(grouped[TodoUrgency.Upcoming]);
    }

    [Fact]
    public void OrderUnfinished_重要优先其次拖得最久的在前()
    {
        var items = new List<(DateTime, TodoItem)>
        {
            (Today.AddDays(-1), Todo("昨天普通")),
            (Today.AddDays(-30), Todo("很久前普通")),
            (Today.AddDays(-2), Todo("前天重要", priority: "重要"))
        };

        var ordered = OrderUnfinished(items);

        Assert.Equal("前天重要", ordered[0].Todo.Text);
        Assert.Equal("很久前普通", ordered[1].Todo.Text);
        Assert.Equal("昨天普通", ordered[2].Todo.Text);
    }

    [Fact]
    public void GroupUnfinished_三个分组永远存在便于调用方直接索引()
    {
        var grouped = GroupUnfinished([], Today);

        Assert.Empty(grouped[TodoUrgency.Overdue]);
        Assert.Empty(grouped[TodoUrgency.Today]);
        Assert.Empty(grouped[TodoUrgency.Upcoming]);
    }
}

/// <summary>
/// 日期数字的颜色通道只承载法定属性。这里把优先级钉死，
/// 避免以后有人"顺手"把周末或今天也塞进这条通道。
/// </summary>
public class DayEmphasisTests
{
    [Fact]
    public void 普通本月日期()
    {
        Assert.Equal(DayEmphasis.Normal, ResolveEmphasis(isCurrentMonth: true, isOffDay: null));
    }

    [Fact]
    public void 非本月压暗()
    {
        Assert.Equal(DayEmphasis.Adjacent, ResolveEmphasis(isCurrentMonth: false, isOffDay: null));
    }

    // ⚠️ ResolveEmphasis 只决定**色相**，不决定透明度。
    // 透明度由"是不是本月"单独决定（见 MainWindow.CreateDayContent）：
    // 非本月一律压暗，即使那天放假或调休 —— 否则跨月首尾行会冒出几个满亮度的
    // 彩色数字，整行深浅不一。别把透明度逻辑塞回这个函数里。

    [Fact]
    public void 非本月的放假仍然保留青色色相()
    {
        Assert.Equal(DayEmphasis.HolidayOff, ResolveEmphasis(isCurrentMonth: false, isOffDay: true));
        Assert.Equal(DayEmphasis.HolidayOff, ResolveEmphasis(isCurrentMonth: true, isOffDay: true));
    }

    [Fact]
    public void 非本月的调休仍然保留橙色色相()
    {
        Assert.Equal(DayEmphasis.HolidayWork, ResolveEmphasis(isCurrentMonth: false, isOffDay: false));
        Assert.Equal(DayEmphasis.HolidayWork, ResolveEmphasis(isCurrentMonth: true, isOffDay: false));
    }

    [Theory]
    [InlineData(DayEmphasis.HolidayOff, " · 休")]
    [InlineData(DayEmphasis.HolidayWork, " · 班")]
    [InlineData(DayEmphasis.Normal, "")]
    [InlineData(DayEmphasis.Adjacent, "")]
    public void 农历行的假日后缀(DayEmphasis emphasis, string expected)
    {
        Assert.Equal(expected, HolidaySuffix(emphasis));
    }
}

/// <summary>
/// 「5+2」分割线的列位置。周日起始时周末被拆到两端，是最容易漏的分支。
/// </summary>
public class WeekendDividerTests
{
    [Fact]
    public void 周一起始时周末相邻_一条线()
    {
        Assert.Equal([5], WeekendDividerColumns(startWithMonday: true));
    }

    [Fact]
    public void 周日起始时周末分居两端_必须两条线()
    {
        Assert.Equal([1, 6], WeekendDividerColumns(startWithMonday: false));
    }

    [Theory]
    [InlineData(0, false)][InlineData(4, false)]
    [InlineData(5, true)][InlineData(6, true)]
    public void 周一起始的周末列(int column, bool expected)
    {
        Assert.Equal(expected, IsWeekendColumn(column, startWithMonday: true));
    }

    [Theory]
    [InlineData(0, true)][InlineData(1, false)]
    [InlineData(5, false)][InlineData(6, true)]
    public void 周日起始的周末列(int column, bool expected)
    {
        Assert.Equal(expected, IsWeekendColumn(column, startWithMonday: false));
    }

    [Fact]
    public void 分割线数量与周末列数一致()
    {
        foreach (var mondayFirst in new[] { true, false })
        {
            var weekendCols = Enumerable.Range(0, 7).Count(c => IsWeekendColumn(c, mondayFirst));
            Assert.Equal(2, weekendCols);
        }
    }

    // ── 今日块的文案 ──────────────────────────────────────────────

    [Theory]
    [InlineData(2026, 8, 14, "周五")]
    [InlineData(2026, 1, 4, "周日")]
    [InlineData(2026, 10, 1, "周四")]
    public void 今日块日期行_浏览当月时只给星期(int year, int month, int day, string expected)
    {
        var today = new DateTime(year, month, day);
        var visibleMonth = new DateTime(year, month, 1);
        Assert.Equal(expected, BuildTodayDateLine(today, visibleMonth));
    }

    /// <summary>
    /// 翻到别的月份时必须补回月日：那时屏幕上再没有别处写着今天是几月几号，
    /// 只剩今日块里一个孤零零的大号数字。
    /// </summary>
    [Theory]
    [InlineData(2026, 12, 1, "8月14日 · 周五")]  // 同年不同月
    [InlineData(2027, 8, 1, "8月14日 · 周五")]   // 同月不同年
    public void 今日块日期行_翻到别的月份时补回月日(int visibleYear, int visibleMonth, int visibleDay, string expected)
    {
        var today = new DateTime(2026, 8, 14);
        Assert.Equal(expected, BuildTodayDateLine(today, new DateTime(visibleYear, visibleMonth, visibleDay)));
    }

    [Fact]
    public void 今日块农历行_节日与节气依次追加()
    {
        Assert.Equal(
            "农历六月廿二",
            BuildTodayLunarLine(new LunarInfo("六", "廿二", null, null)));

        Assert.Equal(
            "农历七月初一 · 立秋",
            BuildTodayLunarLine(new LunarInfo("七", "初一", null, "立秋")));

        Assert.Equal(
            "农历八月十五 · 中秋 · 秋分",
            BuildTodayLunarLine(new LunarInfo("八", "十五", "中秋", "秋分")));
    }

    [Fact]
    public void 今日块农历行_月名为空时不输出农历段()
    {
        Assert.Equal("立秋", BuildTodayLunarLine(new LunarInfo(string.Empty, string.Empty, null, "立秋")));
        Assert.Equal(string.Empty, BuildTodayLunarLine(new LunarInfo(string.Empty, string.Empty, null, null)));
    }

    [Theory]
    [InlineData("国庆节", true, "国庆节 · 休")]
    [InlineData("国庆节", false, "国庆节 · 班")]
    public void 今日块假日标签(string name, bool isOffDay, string expected)
    {
        Assert.Equal(expected, BuildTodayHolidayChip(name, isOffDay));
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(-1, "")]
    [InlineData(1, "未完成 1 项")]
    [InlineData(12, "未完成 12 项")]
    public void 今日块未完成计数(int count, string expected)
    {
        Assert.Equal(expected, BuildTodayUnfinishedLabel(count));
    }
}
