namespace TransparentCalendar.Models;

/// <summary>
/// 日期数字的强调类型 —— 这条通道只承载**法定属性**。
/// 周末不在此列（它用 5+2 分割线表达），今天也不在此列（它用填充形状表达），
/// 目的是让颜色通道不被多重语义占用。
/// </summary>
public enum DayEmphasis
{
    /// <summary>普通日期，用设置里的文字颜色。</summary>
    Normal,

    /// <summary>非本月，压暗。</summary>
    Adjacent,

    /// <summary>法定放假。</summary>
    HolidayOff,

    /// <summary>调休上班。</summary>
    HolidayWork,

    /// <summary>今天。优先级最高，盖过法定属性。</summary>
    Today
}

/// <summary>未完成待办的紧急度分组。</summary>
public enum TodoUrgency
{
    /// <summary>日期已过 —— 逾期未完成。</summary>
    Overdue,

    /// <summary>就是今天。</summary>
    Today,

    /// <summary>未来的安排。</summary>
    Upcoming
}

/// <summary>
/// 日历条目的查询、匹配与摘要 —— 全部是无 UI 依赖的纯函数，便于单元测试。
/// 从 MainWindow 抽出，界面代码通过 `using static` 直接调用。
/// </summary>
public static class CalendarQuery
{
    /// <summary>优先级 "重要" 是**数据**而非展示文案，改动会让已有的 calendar-data.json 失效。</summary>
    public const string ImportantPriority = "重要";
    public const string NormalPriority = "普通";

    private const int PreviewMaxLength = 42;

    public static bool IsImportantTodo(TodoItem todo)
    {
        return string.Equals(todo.Priority, ImportantPriority, StringComparison.Ordinal);
    }

    public static bool EntryHasContent(CalendarEntry entry)
    {
        return entry.Todos.Any(todo => !string.IsNullOrWhiteSpace(todo.Text))
            || !string.IsNullOrWhiteSpace(entry.Diary);
    }

    public static bool EntryMatchesSearch(CalendarEntry entry, string searchText)
    {
        return string.IsNullOrWhiteSpace(searchText)
            || (!string.IsNullOrWhiteSpace(entry.Diary)
                && entry.Diary.Contains(searchText, StringComparison.CurrentCultureIgnoreCase))
            || entry.Todos.Any(todo => TodoMatchesSearch(todo, searchText));
    }

    public static bool TodoMatchesSearch(TodoItem todo, string searchText)
    {
        return string.IsNullOrWhiteSpace(searchText)
            || todo.Text.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)
            || todo.Priority.Contains(searchText, StringComparison.CurrentCultureIgnoreCase);
    }

    public static string BuildTodoSummary(TodoItem todo)
    {
        var priority = IsImportantTodo(todo) ? ImportantPriority : NormalPriority;
        var postponed = string.IsNullOrWhiteSpace(todo.PostponedLabel) ? string.Empty : $" · {todo.PostponedLabel}";
        return $"{priority} · {todo.Text.Trim()}{postponed}";
    }

    public static string BuildEntrySummary(CalendarEntry entry)
    {
        var todoCount = entry.Todos.Count(todo => !string.IsNullOrWhiteSpace(todo.Text));
        var unfinishedCount = entry.Todos.Count(todo => !todo.IsDone && !string.IsNullOrWhiteSpace(todo.Text));
        var parts = new List<string>();
        if (todoCount > 0)
        {
            parts.Add($"待办 {todoCount} 项，未完成 {unfinishedCount} 项");
        }

        if (!string.IsNullOrWhiteSpace(entry.Diary))
        {
            parts.Add($"日记：{PreviewText(entry.Diary)}");
        }

        return parts.Count == 0 ? "无内容" : string.Join("；", parts);
    }

    public static string PreviewText(string text)
    {
        var normalized = string.Join(
            " ",
            text.Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= PreviewMaxLength ? normalized : $"{normalized[..PreviewMaxLength]}...";
    }

    public static string BuildTodayTodoText(TodoItem todo, bool isImportant)
    {
        var prefix = isImportant ? $"{ImportantPriority}：" : string.Empty;
        var postponed = string.IsNullOrWhiteSpace(todo.PostponedLabel) ? string.Empty : $"（{todo.PostponedLabel}）";
        return $"{prefix}{todo.Text.Trim()}{postponed}";
    }

    // ── 今日块的文案 ──────────────────────────────────────────────
    // 「今天」的识别任务已经从日期格搬到了月历上方那块常驻的今日块里，格子里只剩
    // 数字加粗这一声。文案都收在这里，界面代码不再自己拼字符串。

    /// <summary>今日块里没有未完成待办时的提示。</summary>
    public const string TodayEmptyHint = "今天没有待办";

    /// <summary>用「周五」而不是 <c>dddd</c> 的「星期五」—— 短一截，今日块那一行才排得开。</summary>
    private static readonly string[] WeekdayNames = ["周日", "周一", "周二", "周三", "周四", "周五", "周六"];

    /// <summary>
    /// 今日块第一行。
    ///
    /// 浏览的正是当月时只给星期（<c>周一</c>）—— 大号日号已经说了"日"，顶栏说了"年月"，
    /// 再写一遍 <c>8月17日</c> 就是同一块里说三遍。
    ///
    /// 翻到别的月份时补回月日（<c>8月17日 · 周一</c>）：那时屏幕上再没有别处告诉你今天是几月几号，
    /// 只剩一个孤零零的大号数字，容易被读成"你正在看的那个月的 17 号"。
    /// </summary>
    public static string BuildTodayDateLine(DateTime today, DateTime visibleMonth)
    {
        var weekday = WeekdayNames[(int)today.DayOfWeek];
        var browsingThisMonth = visibleMonth.Year == today.Year && visibleMonth.Month == today.Month;
        return browsingThisMonth ? weekday : $"{today.Month}月{today.Day}日 · {weekday}";
    }

    /// <summary>今日块第二行：<c>农历六月廿二 · 立秋</c>。节日与节气存在时依次追加。</summary>
    public static string BuildTodayLunarLine(LunarInfo lunar)
    {
        var parts = new List<string>();
        if (lunar.MonthName.Length > 0)
        {
            parts.Add($"农历{lunar.MonthName}月{lunar.DayName}");
        }

        if (lunar.Festival is not null)
        {
            parts.Add(lunar.Festival);
        }

        if (lunar.SolarTerm is not null)
        {
            parts.Add(lunar.SolarTerm);
        }

        return string.Join(" · ", parts);
    }

    /// <summary>今日块上的假日标签：<c>国庆节 · 休</c> / <c>国庆节 · 班</c>。</summary>
    public static string BuildTodayHolidayChip(string holidayName, bool isOffDay)
    {
        return $"{holidayName} · {(isOffDay ? "休" : "班")}";
    }

    /// <summary>今日块右侧的未完成计数；一件都没有时为空串（那时显示 <see cref="TodayEmptyHint"/>）。</summary>
    public static string BuildTodayUnfinishedLabel(int unfinishedCount)
    {
        return unfinishedCount <= 0 ? string.Empty : $"未完成 {unfinishedCount} 项";
    }

    /// <summary>
    /// 今日块右侧摘要要显示哪几条：**今天的未完成项优先，不足则补当月其余未完成项**。
    ///
    /// 今日块被压成一行之后，只有一两条待办时右边会空掉三分之二。与其留白，不如把
    /// 当月其他还欠着的事顺带带出来 —— 它本来就是"今天要面对什么"这块的一部分。
    ///
    /// 返回的每一项都带日期，界面据此给非今天的项加日期前缀，否则会被读成今天的。
    /// 排序复用 <see cref="OrderUnfinished"/>，不要另写一套。
    /// </summary>
    public static List<(DateTime Date, TodoItem Todo)> BuildTodayPanelSummary(
        IReadOnlyDictionary<string, CalendarEntry> entries,
        DateTime today,
        int maxCount)
    {
        if (maxCount <= 0)
        {
            return [];
        }

        var unfinished = new List<(DateTime Date, TodoItem Todo)>();
        foreach (var (key, entry) in entries)
        {
            if (DateKeys.ParseDateKey(key) is not { } date)
            {
                continue;
            }

            foreach (var todo in entry.Todos)
            {
                if (!todo.IsDone && !string.IsNullOrWhiteSpace(todo.Text))
                {
                    unfinished.Add((date, todo));
                }
            }
        }

        var todayItems = OrderUnfinished(unfinished.Where(item => item.Date.Date == today.Date));
        if (todayItems.Count >= maxCount)
        {
            return todayItems.Take(maxCount).ToList();
        }

        // 补位的只取当月，且逾期的排在未来的前面（OrderUnfinished 已经按日期升序）
        var sameMonth = OrderUnfinished(unfinished.Where(item =>
            item.Date.Date != today.Date
            && item.Date.Year == today.Year
            && item.Date.Month == today.Month));

        return todayItems.Concat(sameMonth).Take(maxCount).ToList();
    }

    /// <summary>今日块摘要里非今天那几条的前缀（<c>19日 · </c>），今天的不加。</summary>
    public static string BuildSummaryDatePrefix(DateTime date, DateTime today)
    {
        return date.Date == today.Date ? string.Empty : $"{date.Day}日 · ";
    }

    private const int ToolTipTodoCount = 3;

    /// <summary>
    /// 日期数字的强调类型。优先级：<c>今天 &gt; 放假 &gt; 调休 &gt; 非本月 &gt; 普通</c>。
    ///
    /// **今天排在最前**：一个数字只能有一种颜色，今天恰好是放假日时以"今天"为准 ——
    /// 那天的法定属性由农历行末尾的 <c>· 休</c> / <c>· 班</c> 承担，信息不丢。
    ///
    /// 放假/调休**故意排在非本月之前**：跨月的首尾行会显示邻月日期，而"邻月的 10 月 1 日放假"
    /// 依然是用户需要看到的信息，不该因为不在当月就被压成灰色。
    /// </summary>
    public static DayEmphasis ResolveEmphasis(bool isCurrentMonth, bool? isOffDay, bool isToday = false)
    {
        if (isToday)
        {
            return DayEmphasis.Today;
        }

        if (isOffDay is true)
        {
            return DayEmphasis.HolidayOff;
        }

        if (isOffDay is false)
        {
            return DayEmphasis.HolidayWork;
        }

        return isCurrentMonth ? DayEmphasis.Normal : DayEmphasis.Adjacent;
    }

    /// <summary>
    /// 「5+2」竖分割线该画在哪几列的左缘（0-based 列号）。
    ///
    /// 周末列是否相邻取决于一周从哪天开始，这是个容易漏掉的分支：
    /// <list type="bullet">
    /// <item>周一起始 → <c>一二三四五 | 六日</c>，周末相邻，一条线</item>
    /// <item>周日起始 → <c>日 | 一二三四五 | 六</c>，周末被拆到两端，需要**两条**线</item>
    /// </list>
    /// </summary>
    public static int[] WeekendDividerColumns(bool startWithMonday)
    {
        return startWithMonday ? [5] : [1, 6];
    }

    /// <summary>某列是否为周末列（0-based）。</summary>
    public static bool IsWeekendColumn(int column, bool startWithMonday)
    {
        return startWithMonday ? column >= 5 : column is 0 or 6;
    }

    /// <summary>
    /// 农历行末尾追加的假日后缀（"· 休" / "· 班"），没有则为空。
    ///
    /// 直接认 <paramref name="isOffDay"/> 而不是 <see cref="DayEmphasis"/>：今天的数字颜色
    /// 被"今天"占用后，emphasis 已经不再携带法定属性，但后缀**必须照常显示** ——
    /// 那是今天当天唯一还在表达"休/班"的地方。
    /// </summary>
    public static string HolidaySuffix(bool? isOffDay)
    {
        return isOffDay switch
        {
            true => " · 休",
            false => " · 班",
            _ => string.Empty
        };
    }

    /// <summary>
    /// 日期格的悬停提示。除了计数还带上实际内容 —— 只报"待办 N 项未完成"的话，
    /// 用户还是得点开才知道是什么事。
    /// </summary>
    public static string BuildDayToolTip(DateTime date, CalendarEntry? entry, DateTime today)
    {
        var lines = new List<string>
        {
            date.ToString("yyyy-MM-dd dddd", System.Globalization.CultureInfo.GetCultureInfo("zh-CN"))
        };

        if (entry is null || !EntryHasContent(entry))
        {
            return lines[0];
        }

        var unfinished = entry.Todos
            .Where(todo => !todo.IsDone && !string.IsNullOrWhiteSpace(todo.Text))
            .ToList();

        if (unfinished.Count > 0)
        {
            var overdue = ClassifyUrgency(date, today) == TodoUrgency.Overdue
                ? $"（已逾期 {OverdueDays(date, today)} 天）"
                : string.Empty;
            lines.Add($"未完成 {unfinished.Count} 项{overdue}：");

            foreach (var todo in unfinished.Take(ToolTipTodoCount))
            {
                var mark = IsImportantTodo(todo) ? "★ " : "· ";
                lines.Add($"  {mark}{todo.Text.Trim()}");
            }

            if (unfinished.Count > ToolTipTodoCount)
            {
                lines.Add($"  …… 还有 {unfinished.Count - ToolTipTodoCount} 项");
            }
        }

        var doneCount = entry.Todos.Count(todo => todo.IsDone && !string.IsNullOrWhiteSpace(todo.Text));
        if (doneCount > 0)
        {
            lines.Add($"已完成 {doneCount} 项");
        }

        if (!string.IsNullOrWhiteSpace(entry.Diary))
        {
            lines.Add($"日记：{PreviewText(entry.Diary)}");
        }

        return string.Join("\n", lines);
    }

    public static TodoUrgency ClassifyUrgency(DateTime date, DateTime today)
    {
        if (date.Date < today.Date)
        {
            return TodoUrgency.Overdue;
        }

        return date.Date == today.Date ? TodoUrgency.Today : TodoUrgency.Upcoming;
    }

    /// <summary>
    /// 未完成待办的排序：重要优先，其次按日期升序（逾期组里即"拖得最久的排最前"）。
    /// 注意分组本身必须由调用方**分别限流**，否则大量逾期项会把"今天"挤出可视范围
    /// —— 这正是早先 `OrderBy(Date).Take(30)` 的缺陷。
    /// </summary>
    public static List<(DateTime Date, TodoItem Todo)> OrderUnfinished(
        IEnumerable<(DateTime Date, TodoItem Todo)> items)
    {
        return items
            .OrderByDescending(item => IsImportantTodo(item.Todo))
            .ThenBy(item => item.Date)
            .ToList();
    }

    /// <summary>把未完成待办按紧急度分成三组，组内已排好序。</summary>
    public static Dictionary<TodoUrgency, List<(DateTime Date, TodoItem Todo)>> GroupUnfinished(
        IEnumerable<(DateTime Date, TodoItem Todo)> items,
        DateTime today)
    {
        var grouped = new Dictionary<TodoUrgency, List<(DateTime Date, TodoItem Todo)>>
        {
            [TodoUrgency.Overdue] = [],
            [TodoUrgency.Today] = [],
            [TodoUrgency.Upcoming] = []
        };

        foreach (var item in OrderUnfinished(items))
        {
            grouped[ClassifyUrgency(item.Date, today)].Add(item);
        }

        return grouped;
    }

    /// <summary>逾期天数，用于在列表里明示"拖了多久"。</summary>
    public static int OverdueDays(DateTime date, DateTime today)
    {
        return Math.Max(0, (today.Date - date.Date).Days);
    }

    /// <summary>推迟天数始终相对**最初**日期累计，而不是相对上一次推迟。</summary>
    public static int CalculatePostponedDays(string postponedFromDate, DateTime targetDate)
    {
        return DateKeys.ParseDateKey(postponedFromDate) is { } fromDate
            ? Math.Max(1, (targetDate.Date - fromDate.Date).Days)
            : 1;
    }
}
