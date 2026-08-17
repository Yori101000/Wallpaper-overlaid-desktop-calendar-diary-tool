using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using TransparentCalendar.Models;
using TransparentCalendar.Native;
using TransparentCalendar.Services;
using TransparentCalendar.Views;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfCursors = System.Windows.Input.Cursors;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfPoint = System.Windows.Point;
using WpfTypography = System.Windows.Documents.Typography;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;
using static TransparentCalendar.Models.CalendarQuery;
using static TransparentCalendar.Models.DateKeys;

namespace TransparentCalendar;

// 月历视图：周头、42 个日期格的构建与更新、今日待办、日期编辑器。
public partial class MainWindow : Window
{
    /// <summary>周头当标签处理：固定 11px、SemiBold、加字距、压低对比 —— 不是正文。</summary>
    private void RenderWeekHeader()
    {
        WeekHeaderGrid.Children.Clear();
        var days = _settings.StartWithMonday
            ? ["一", "二", "三", "四", "五", "六", "日"]
            : new[] { "日", "一", "二", "三", "四", "五", "六" };

        for (var i = 0; i < days.Length; i++)
        {
            var isWeekend = IsWeekendColumn(i, _settings.StartWithMonday);
            WeekHeaderGrid.Children.Add(new TextBlock
            {
                Text = days[i],
                // 周头原先压到 0.42/0.58，比它下面的农历行还淡 —— 层级反了。
                // 它是读懂整张表的钥匙，必须比格子里的次要信息更清楚。
                Foreground = TextBrush(_settings.TextOpacity * (isWeekend ? 0.86 : 0.68)),
                FontSize = WeekHeaderFontSize,
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = WpfVerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold,
                Effect = OptionalTextShadow(_settings.TextOpacity * 0.7)
            });
        }

        RebuildWeekendDividers();
    }

    /// <summary>42 个日期按钮只构建一次，之后只替换内容 —— 模板与动画触发器的展开很贵。</summary>
    private void EnsureDayButtons()
    {
        if (_dayButtons is not null)
        {
            return;
        }

        CalendarGrid.Children.Clear();
        CalendarGrid.RowDefinitions.Clear();
        CalendarGrid.ColumnDefinitions.Clear();

        for (var col = 0; col < 7; col++)
        {
            CalendarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (var row = 0; row < 6; row++)
        {
            CalendarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }

        _dayButtons = new WpfButton[DayCellCount];
        var style = (Style)FindResource("CalendarButtonStyle");
        for (var i = 0; i < DayCellCount; i++)
        {
            var button = new WpfButton
            {
                Style = style,
                HorizontalContentAlignment = WpfHorizontalAlignment.Stretch,
                VerticalContentAlignment = WpfVerticalAlignment.Stretch
            };
            button.Click += DayButton_Click;
            Grid.SetRow(button, i / 7);
            Grid.SetColumn(button, i % 7);
            CalendarGrid.Children.Add(button);
            _dayButtons[i] = button;
        }

        RebuildWeekendDividers();
    }

    private void RenderCalendar()
    {
        EnsureDayButtons();

        UpdateMonthTitle();
        MonthTitle.Foreground = TextBrush(_settings.TextOpacity);

        var startDate = _visibleMonth.AddDays(-GetFirstDayOffset(_visibleMonth));

        if (_settings.ShowStatutoryHolidays)
        {
            // 首尾格可能跨年，两个年份都要确保。数据未就绪时后台拉取，就绪后回调重绘。
            _holidays.EnsureYear(startDate.Year);
            _holidays.EnsureYear(startDate.AddDays(DayCellCount - 1).Year);

            // 预热相邻年份。没有这一步，翻到未缓存的年份时要等一次网络往返（最长 5 秒），
            // 这段时间里放假日会先渲染成普通白色再"跳"成青色。
            // EnsureYear 对已在内存/正在请求的年份是空操作，所以每次渲染都调用没有代价。
            var visibleYear = _visibleMonth.Year;
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                () =>
                {
                    _holidays.EnsureYear(visibleYear - 1);
                    _holidays.EnsureYear(visibleYear + 1);
                });
        }

        for (var i = 0; i < DayCellCount; i++)
        {
            UpdateDayButton(_dayButtons![i], startDate.AddDays(i));
        }

        RenderTodayPanel();

        // 列表面板不可见时没必要重建 —— 翻月份原本会白白扫描三遍全部条目。
        if (_mode == ViewMode.List)
        {
            RenderListView();
        }
    }

    private void UpdateDayButton(WpfButton button, DateTime date)
    {
        var key = DateKey(date);
        var isCurrentMonth = date.Month == _visibleMonth.Month;
        _entries.TryGetValue(key, out var entry);

        var todos = entry?.Todos
            .Where(todo => !string.IsNullOrWhiteSpace(todo.Text))
            .ToList() ?? [];
        var unfinishedCount = todos.Count(todo => !todo.IsDone);
        var hasImportantTodo = todos.Any(todo => IsImportantTodo(todo) && !todo.IsDone);
        var hasDiary = entry is not null && !string.IsNullOrWhiteSpace(entry.Diary);

        button.Tag = date;
        button.Content = CreateDayContent(date, isCurrentMonth, hasDiary, unfinishedCount, hasImportantTodo);
        button.ToolTip = BuildDayToolTip(date, entry) + BuildAlmanacToolTip(date);

        // Background / BorderBrush / BorderThickness 一概不动 —— 样式里已是透明与 0，
        // 填充圆角矩形这一族留给 hover 层独占，「今天」由今日块承载。
    }

    private Grid CreateDayContent(
        DateTime date,
        bool isCurrentMonth,
        bool hasDiary,
        int unfinishedCount,
        bool hasImportantTodo)
    {
        var isToday = date.Date == DateTime.Today;
        var holiday = GetHoliday(date);
        var emphasis = ResolveEmphasis(isCurrentMonth, holiday?.IsOffDay);

        // 数字的颜色只承载**法定属性**（放假/调休）。周末交给 5+2 分割线，
        // 今天交给格子的填充与内描边 —— 三者各占一条通道，互不侵占。
        //
        // 透明度与色相是两件独立的事：非本月**一律**降透明度，即使那天放假或调休 ——
        // 否则跨月首尾行会冒出几个满亮度的彩色数字，整行深浅不一。
        var opacity = isCurrentMonth
            ? _settings.TextOpacity
            : _settings.TextOpacity * AdjacentMonthOpacity;

        // 走 GetBrush 而不是用静态冻结画刷，透明度才能作用到假日色上（缓存仍然生效）
        var numberBrush = emphasis switch
        {
            DayEmphasis.HolidayOff => GetBrush(_holidayOffColor, opacity),
            DayEmphasis.HolidayWork => GetBrush(_holidayWorkColor, opacity),
            _ => TextBrush(opacity)
        };

        var lunarLabel = _settings.ShowLunar ? LunarCalendar.GetInfo(date) : null;
        var holidaySuffix = HolidaySuffix(emphasis);

        // 今天的这一行直接写「今天」两个字。
        //
        // 这是文字而不是装饰 —— 装饰那一族（填充、描边、圆、横线）前后被否掉十种，
        // 而这一行本来就在，写字不占新空间。今天的农历在今日块里已经完整给出（农历七月初二），
        // 格子里不必重复；假日后缀仍然保留。
        var almanacText = isToday
            ? TodayCellLabel + holidaySuffix
            : (lunarLabel?.Label ?? string.Empty) + holidaySuffix;
        var almanacFontSize = ScaledFont(FontScale.Almanac, 9);

        // 三行**定高**：数字 / 农历 / 圆点。
        //
        // 原先是一个居中的 StackPanel，子元素数量随内容变化（有无农历、0~2 个圆点），
        // 于是同一行里有内容的格子数字被顶高、空格子偏低 —— 整行数字高低不齐，
        // 这是"糊"的主因，也让任何「今天」标记都显得脏。
        //
        // 农历行**恒定占位**：今天那一格永远要写「今天」，所以这一行总归是需要的。
        // 更重要的是它必须对 42 格一视同仁 —— 按格判断（这格有没有农历/假日）会让基线参差。

        // 首尾两个 * 行把定高的三行当作一块居中；徽章则靠 RowSpan 覆盖整格，
        // 才能落在格子真正的右上角（若把 content 整体居中，徽章会跟着缩到中间去）。
        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(_settings.FontSize * NumberRowRatio)
        });
        content.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(almanacFontSize * AlmanacRowRatio)
        });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(MarkerRowHeight) });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // 「今天」靠**字号 + 字重 + 满亮度**跳出来，不加任何装饰。
        // 一屏 42 个数字里只靠加粗是找不到的（实测），而放大一档不占新空间：
        // 数字行是定高的，字号变化不会顶动农历行，也不会让整行基线参差。
        var dayNumber = new TextBlock
        {
            Text = date.Day.ToString(CultureInfo.InvariantCulture),
            FontSize = isToday ? _settings.FontSize * TodayNumberScale : _settings.FontSize,
            FontWeight = isToday ? FontWeights.Bold : FontWeights.Medium,
            Foreground = numberBrush,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = WpfVerticalAlignment.Center,
            // LineHeight 恒按**普通字号**算：今天放大一档也不能顶动农历行的位置。
            LineHeight = _settings.FontSize * 1.1,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Effect = OptionalTextShadow(opacity)
        };
        WpfTypography.SetNumeralAlignment(dayNumber, FontNumeralAlignment.Tabular);
        Grid.SetRow(dayNumber, 1);
        content.Children.Add(dayNumber);

        if (almanacText.Length > 0)
        {
            // 今天跟节日节气一样按"高亮"处理：更亮、更粗。
            var isHighlighted = isToday || lunarLabel?.IsHighlighted == true;
            var almanac = new TextBlock
            {
                Text = almanacText,
                FontSize = almanacFontSize,
                Foreground = emphasis switch
                {
                    DayEmphasis.HolidayOff => GetBrush(_holidayOffColor, opacity * 0.9),
                    DayEmphasis.HolidayWork => GetBrush(_holidayWorkColor, opacity * 0.9),
                    _ => isHighlighted ? TextBrush(opacity * 0.88) : TextBrush(opacity * 0.52)
                },
                FontWeight = isHighlighted ? FontWeights.SemiBold : FontWeights.Normal,
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = WpfVerticalAlignment.Center,
                Effect = OptionalTextShadow(opacity * 0.7)
            };
            Grid.SetRow(almanac, 2);
            content.Children.Add(almanac);
        }

        // 只剩日记的青点。待办原先在这里也有一枚琥珀点，与右上角的琥珀徽章
        // 说的是同一件事、还同色 —— 一件事说两遍，格子却只有 ~45px 宽。
        // 待办现在只由徽章表达（带数量，信息更多）。
        if (hasDiary)
        {
            var marker = CreateMarker(DiaryMarkerBrush);
            marker.VerticalAlignment = WpfVerticalAlignment.Center;
            marker.HorizontalAlignment = WpfHorizontalAlignment.Center;
            Grid.SetRow(marker, 3);
            content.Children.Add(marker);
        }

        if (unfinishedCount > 0)
        {
            // 徽章挂在**数字**的右上角，不是格子的右上角。
            //
            // 格子宽 ~94px 而数字居中只有 ~30px 宽，贴在格子角上的徽章离自己的数字太远、
            // 离右邻格的数字反而更近 —— 实测会被读成隔壁那天的待办。
            // 所以按"居中 + 右移一个字宽"来定位，让它明确咬住自己的数字。
            var badgeText = unfinishedCount > 9 ? "9+" : unfinishedCount.ToString(CultureInfo.InvariantCulture);
            var badge = new Border
            {
                Background = hasImportantTodo ? ImportantMarkerBrush : TodoMarkerBrush,
                CornerRadius = new CornerRadius(DayBadgeSize / 2),
                MinWidth = DayBadgeSize,
                Height = DayBadgeSize,
                Padding = new Thickness(3.5, 0, 3.5, 0),
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = WpfVerticalAlignment.Top,
                Margin = new Thickness(_settings.FontSize * BadgeOffsetRatio, -3, 0, 0),
                Child = new TextBlock
                {
                    Text = badgeText,
                    Foreground = BadgeTextBrush,
                    FontSize = 9.5,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = WpfHorizontalAlignment.Center,
                    VerticalAlignment = WpfVerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                }
            };
            // 放在数字那一行（而不是 RowSpan 覆盖整格）：整格 Top 会让徽章飘在数字上方一截。
            Grid.SetRow(badge, 1);
            content.Children.Add(badge);
        }

        return content;
    }

    private static Ellipse CreateMarker(WpfBrush brush)
    {
        return new Ellipse
        {
            Width = 5,
            Height = 5,
            Fill = brush,
            Margin = new Thickness(2, 0, 2, 0)
        };
    }

    /// <summary>
    /// 「5+2」竖分割线。周末不占任何颜色通道，只靠这条线把工作日与周末切开。
    /// 周日起始时周末分居两端，需要两条线 —— 列号由 <see cref="WeekendDividerColumns"/> 决定。
    /// </summary>
    private void RebuildWeekendDividers()
    {
        if (_dayButtons is null)
        {
            return;
        }

        foreach (var stale in _weekendDividers)
        {
            CalendarGrid.Children.Remove(stale);
        }

        _weekendDividers.Clear();

        foreach (var column in WeekendDividerColumns(_settings.StartWithMonday))
        {
            var divider = new WpfRectangle
            {
                Width = 1,
                Fill = WeekendDividerBrush,
                HorizontalAlignment = WpfHorizontalAlignment.Left,
                // 负 1px 让线落在两列的缝隙中央，而不是压在右侧格子的边上
                Margin = new Thickness(-1, 3, 0, 3),
                IsHitTestVisible = false
            };
            Grid.SetColumn(divider, column);
            Grid.SetRow(divider, 0);
            Grid.SetRowSpan(divider, 6);
            CalendarGrid.Children.Add(divider);
            _weekendDividers.Add(divider);
        }
    }

    private static string BuildDayToolTip(DateTime date, CalendarEntry? entry)
    {
        return CalendarQuery.BuildDayToolTip(date, entry, DateTime.Today);
    }

    /// <summary>农历与法定假日追加到提示末尾。</summary>
    private string BuildAlmanacToolTip(DateTime date)
    {
        var parts = new List<string>();

        if (_settings.ShowLunar)
        {
            var lunar = LunarCalendar.GetInfo(date);
            var detail = lunar.MonthName.Length > 0 ? $"农历{lunar.MonthName}月{lunar.DayName}" : string.Empty;
            if (lunar.Festival is not null) detail += $" · {lunar.Festival}";
            if (lunar.SolarTerm is not null) detail += $" · {lunar.SolarTerm}";
            if (detail.Length > 0) parts.Add(detail);
        }

        if (GetHoliday(date) is { } holiday)
        {
            parts.Add(holiday.IsOffDay ? $"{holiday.Name}放假" : $"{holiday.Name}调休上班");
        }

        return parts.Count == 0 ? string.Empty : "\n" + string.Join("\n", parts);
    }

    /// <summary>只读已载入内存的假日数据；未就绪时返回 null，绝不为此阻塞渲染。</summary>
    private HolidayInfo? GetHoliday(DateTime date)
    {
        return _settings.ShowStatutoryHolidays ? _holidays.Find(date) : null;
    }

    /// <summary>
    /// 今日块。「今天」的识别任务在这里，日期格里只留数字加粗那一声 ——
    /// 42 个格子每个只有 ~45×52px，塞不下第五样装饰。
    ///
    /// 它读 <see cref="DateTime.Today"/> 而不是 <c>_visibleMonth</c>：翻月份时内容不变，
    /// 这是有意的，"今天"不该跟着浏览的月份跑。
    /// </summary>
    private void RenderTodayPanel()
    {
        var today = DateTime.Today;
        var opacity = _settings.TextOpacity;

        TodayDayNumber.Text = today.Day.ToString(CultureInfo.InvariantCulture);
        TodayDayNumber.FontSize = _settings.FontSize * FontScale.TodayNumber;
        TodayDayNumber.Foreground = TextBrush(Math.Min(1, opacity + 0.08));
        TodayDayNumber.Effect = OptionalTextShadow(opacity);

        TodayDateLine.Text = BuildTodayDateLine(today, _visibleMonth);
        TodayDateLine.FontSize = ScaledFont(FontScale.TodayMeta);
        TodayDateLine.Foreground = TextBrush(opacity);
        TodayDateLine.Effect = OptionalTextShadow(opacity * 0.7);

        var lunarLine = _settings.ShowLunar ? BuildTodayLunarLine(LunarCalendar.GetInfo(today)) : string.Empty;
        TodayLunarLine.Text = lunarLine;
        TodayLunarLine.FontSize = ScaledFont(FontScale.TodayMeta);
        TodayLunarLine.Foreground = TextBrush(opacity * 0.6);
        TodayLunarLine.Effect = OptionalTextShadow(opacity * 0.6);
        TodayLunarLine.Visibility = lunarLine.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        RenderTodayHolidayChip(today, opacity);
        RenderTodayTodos(today, opacity);
    }

    /// <summary>
    /// 假日标签。这是唯一允许颜色参与「今天」表达的地方 —— 它承载的是法定属性
    /// （通道一），不是"今天"本身，所以用的也是同一对经过避让的假日色。
    /// </summary>
    private void RenderTodayHolidayChip(DateTime today, double opacity)
    {
        if (GetHoliday(today) is not { } holiday)
        {
            TodayHolidayChip.Visibility = Visibility.Collapsed;
            return;
        }

        var color = holiday.IsOffDay ? _holidayOffColor : _holidayWorkColor;
        TodayHolidayChip.Visibility = Visibility.Visible;
        TodayHolidayChip.Background = GetBrush(color, 0.16);
        TodayHolidayText.Text = BuildTodayHolidayChip(holiday.Name, holiday.IsOffDay);
        TodayHolidayText.FontSize = ScaledFont(FontScale.TodayMeta, 10);
        TodayHolidayText.Foreground = GetBrush(color, Math.Min(1, opacity + 0.05));
    }

    private void RenderTodayTodos(DateTime today, double opacity)
    {
        TodayTodoItems.Children.Clear();

        var unfinishedTodos = _entries.TryGetValue(DateKey(today), out var entry)
            ? entry.Todos.Where(todo => !todo.IsDone && !string.IsNullOrWhiteSpace(todo.Text)).ToList()
            : [];

        // 一件都没有时把提示放在**同一行**右侧，而不是另起一行 ——
        // 否则今日块凭空高出一行，右半边还是空的。
        var hasTodos = unfinishedTodos.Count > 0;
        TodayTodoTitle.Text = hasTodos
            ? BuildTodayUnfinishedLabel(unfinishedTodos.Count)
            : CalendarQuery.TodayEmptyHint;
        TodayTodoTitle.Foreground = TextBrush(opacity * (hasTodos ? 0.75 : 0.45));
        TodayTodoTitle.FontSize = ScaledFont(FontScale.TodayMeta);
        TodayTodoTitle.Effect = OptionalTextShadow(opacity * 0.7);
        TodayTodoItems.Visibility = hasTodos ? Visibility.Visible : Visibility.Collapsed;

        if (!hasTodos)
        {
            return;
        }

        // 摘要与计数排在同一行，装不下就裁剪 —— 今日块只占一行，不再向下长。
        foreach (var todo in unfinishedTodos.Take(TodayTodoPreviewCount))
        {
            var isImportant = IsImportantTodo(todo);
            TodayTodoItems.Children.Add(new TextBlock
            {
                Text = BuildTodayTodoText(todo, isImportant),
                Foreground = isImportant ? ImportantMarkerBrush : TextBrush(opacity * 0.92),
                FontSize = ScaledFont(FontScale.TodayMeta),
                Margin = new Thickness(0, 0, 14, 0),
                VerticalAlignment = WpfVerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Effect = OptionalTextShadow(opacity * 0.7)
            });
        }

        if (unfinishedTodos.Count > TodayTodoPreviewCount)
        {
            TodayTodoItems.Children.Add(new TextBlock
            {
                Text = $"+{unfinishedTodos.Count - TodayTodoPreviewCount}",
                Foreground = TextBrush(opacity * 0.6),
                FontSize = ScaledFont(FontScale.TodayMeta),
                VerticalAlignment = WpfVerticalAlignment.Center,
                Effect = OptionalTextShadow(opacity * 0.7)
            });
        }
    }

    private int GetFirstDayOffset(DateTime month)
    {
        var day = (int)month.DayOfWeek;
        return _settings.StartWithMonday ? (day + 6) % 7 : day;
    }

    private void DayButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: DateTime date })
        {
            OpenDayEditor(date);
        }
    }

    private void OpenDayEditor(DateTime date)
    {
        var key = DateKey(date);
        if (!_entries.TryGetValue(key, out var entry))
        {
            entry = new CalendarEntry { Date = key };
        }

        var editor = new DayEditorWindow(date, entry) { Owner = this };
        if (editor.ShowDialog() != true)
        {
            return;
        }

        // 只有确认保存后才落入字典 —— 否则随手点开日期就会留下一堆空条目。
        _entries[key] = entry;
        entry.UpdatedAt = DateTime.Now;

        foreach (var pending in editor.PendingPostpones)
        {
            AddTodoToDate(pending.TargetDate, pending.Todo);
        }

        PersistEntries();
        RenderCalendar();
    }

    private void AddTodoToDate(DateTime targetDate, TodoItem todo)
    {
        var targetKey = DateKey(targetDate);
        if (!_entries.TryGetValue(targetKey, out var targetEntry))
        {
            targetEntry = new CalendarEntry { Date = targetKey };
            _entries[targetKey] = targetEntry;
        }

        targetEntry.Todos.Add(todo);
        targetEntry.UpdatedAt = DateTime.Now;
    }

    /// <summary>落盘前剔除空条目，避免"点开又取消"的日期把数据文件撑大。</summary>
    private void PersistEntries()
    {
        var emptyKeys = _entries
            .Where(pair => !EntryHasContent(pair.Value))
            .Select(pair => pair.Key)
            .ToList();

        foreach (var key in emptyKeys)
        {
            _entries.Remove(key);
        }

        _storage.SaveEntries(_entries);
    }

    private void PreviousMonth_Click(object sender, RoutedEventArgs e)
    {
        _visibleMonth = _visibleMonth.AddMonths(-1);
        RenderCalendar();
    }

    private void NextMonth_Click(object sender, RoutedEventArgs e)
    {
        _visibleMonth = _visibleMonth.AddMonths(1);
        RenderCalendar();
    }

    private void Today_Click(object sender, RoutedEventArgs e)
    {
        GoToToday();
    }

    /// <summary>
    /// 滚轮翻月。顶栏去掉 ‹ › 之后，这里和 <c>←</c> <c>→</c> 是翻月的全部入口，
    /// 所以别把它挂到别的面板上，也别在这里加节流 —— 一次滚动只翻一个月，跟用户的手一致。
    /// </summary>
    private void CalendarViewPanel_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta == 0)
        {
            return;
        }

        _visibleMonth = _visibleMonth.AddMonths(e.Delta > 0 ? -1 : 1);
        RenderCalendar();
        e.Handled = true;
    }

    private void GoToToday()
    {
        _visibleMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        if (_mode != ViewMode.Calendar)
        {
            _modeBeforeSearch = null;
            SetMode(ViewMode.Calendar);
            return;
        }

        RenderCalendar();
    }
}
