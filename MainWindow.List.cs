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

// 列表（待做）视图：单次遍历分桶出三个分区并渲染。
public partial class MainWindow : Window
{
    private void RenderListView()
    {
        if (ListContentPanel is null)
        {
            return;
        }

        ListContentPanel.Children.Clear();

        // 三个分区原本各自遍历一遍全部条目，这里合并为单次遍历分桶。
        var unfinished = new List<(DateTime Date, TodoItem Todo)>();
        var monthRecords = new List<(DateTime Date, CalendarEntry Entry)>();
        var allRecords = new List<(DateTime Date, CalendarEntry Entry)>();

        foreach (var (key, entry) in _entries)
        {
            if (ParseDateKey(key) is not { } date || !EntryHasContent(entry))
            {
                continue;
            }

            foreach (var todo in entry.Todos)
            {
                if (!todo.IsDone && !string.IsNullOrWhiteSpace(todo.Text) && TodoMatchesSearch(todo, _searchText))
                {
                    unfinished.Add((date, todo));
                }
            }

            if (!EntryMatchesSearch(entry, _searchText))
            {
                continue;
            }

            if (date.Year == _visibleMonth.Year && date.Month == _visibleMonth.Month)
            {
                monthRecords.Add((date, entry));
            }

            allRecords.Add((date, entry));
        }

        AddUnfinishedTodoSection(unfinished);
        AddMonthRecordSection(monthRecords);
        AddRecentRecordSection(allRecords);
    }

    /// <summary>
    /// 按「逾期 / 今天 / 未来」分组渲染，**每组各自限流**。
    /// 早先是全局 `OrderBy(Date).Take(30)`：积压超过 30 条时，列表会被最古老的遗留项
    /// 永久占满，用户根本看不到今天的待办。
    /// </summary>
    private void AddUnfinishedTodoSection(List<(DateTime Date, TodoItem Todo)> items)
    {
        AddListSectionTitle("未完成待办");
        if (items.Count == 0)
        {
            AddListEmptyText("没有未完成待办。");
            return;
        }

        var today = DateTime.Today;
        var grouped = GroupUnfinished(items, today);

        RenderUrgencyGroup("逾期", grouped[TodoUrgency.Overdue], today, ImportantMarkerBrush);
        RenderUrgencyGroup("今天", grouped[TodoUrgency.Today], today, TodoBadgeBrush);
        RenderUrgencyGroup("未来", grouped[TodoUrgency.Upcoming], today, TodoMarkerBrush);
    }

    private void RenderUrgencyGroup(
        string label,
        List<(DateTime Date, TodoItem Todo)> items,
        DateTime today,
        WpfBrush accent)
    {
        if (items.Count == 0)
        {
            return;
        }

        AddListGroupLabel($"{label} · {items.Count} 项");

        // "今天"永远全量展示 —— 它是用户最需要看到的一组，不能被限流挤掉。
        var isToday = string.Equals(label, "今天", StringComparison.Ordinal);
        var limit = _showAllTodos || isToday ? items.Count : UrgencyGroupPreviewCount;

        foreach (var (date, todo) in items.Take(limit))
        {
            var overdueDays = OverdueDays(date, today);
            var title = overdueDays > 0
                ? $"{date:yyyy-MM-dd dddd} · 已逾期 {overdueDays} 天"
                : date.ToString("yyyy-MM-dd dddd", CultureInfo.GetCultureInfo("zh-CN"));

            AddListButton(
                date,
                title,
                BuildTodoSummary(todo),
                IsImportantTodo(todo) ? ImportantMarkerBrush : accent);
        }

        if (items.Count > limit)
        {
            AddShowAllButton(items.Count - limit);
        }
    }

    private void AddListGroupLabel(string text)
    {
        ListContentPanel.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = TextBrush(_settings.TextOpacity * 0.7),
            FontSize = ScaledFont(FontScale.Detail),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(2, 6, 0, 4),
            Effect = OptionalTextShadow(_settings.TextOpacity * 0.6)
        });
    }

    private void AddShowAllButton(int hiddenCount)
    {
        var button = new WpfButton
        {
            Content = $"显示全部（还有 {hiddenCount} 项）",
            HorizontalAlignment = WpfHorizontalAlignment.Left,
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 0, 0, 8),
            FontSize = ScaledFont(FontScale.Detail),
            Foreground = TextBrush(_settings.TextOpacity * 0.85),
            Background = ActionButtonBrush,
            BorderBrush = ActionButtonBorderBrush,
            BorderThickness = new Thickness(1),
            Cursor = WpfCursors.Hand
        };
        button.Click += (_, _) =>
        {
            _showAllTodos = true;
            RenderListView();
        };
        ListContentPanel.Children.Add(button);
    }

    private void AddMonthRecordSection(List<(DateTime Date, CalendarEntry Entry)> items)
    {
        AddListSectionTitle("本月记录");
        if (items.Count == 0)
        {
            AddListEmptyText("本月没有记录。");
            return;
        }

        foreach (var (date, entry) in items.OrderBy(item => item.Date))
        {
            AddListButton(
                date,
                date.ToString("M 月 d 日 dddd", CultureInfo.GetCultureInfo("zh-CN")),
                BuildEntrySummary(entry),
                DiaryMarkerBrush);
        }
    }

    private void AddRecentRecordSection(List<(DateTime Date, CalendarEntry Entry)> items)
    {
        AddListSectionTitle("最近更新");
        if (items.Count == 0)
        {
            AddListEmptyText("没有最近记录。");
            return;
        }

        foreach (var (date, entry) in items.OrderByDescending(item => item.Entry.UpdatedAt).Take(RecentRecordPreviewCount))
        {
            AddListButton(
                date,
                $"{date:yyyy-MM-dd}  更新于 {entry.UpdatedAt:MM-dd HH:mm}",
                BuildEntrySummary(entry),
                TodoMarkerBrush);
        }
    }

    private void AddListSectionTitle(string text)
    {
        ListContentPanel.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = TextBrush(_settings.TextOpacity),
            FontSize = ScaledFont(FontScale.SectionTitle, 14),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 6),
            Effect = OptionalTextShadow(_settings.TextOpacity)
        });
    }

    private void AddListEmptyText(string text)
    {
        ListContentPanel.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = TextBrush(_settings.TextOpacity * 0.55),
            FontSize = ScaledFont(FontScale.Hint),
            Margin = new Thickness(0, 0, 0, 10),
            Effect = OptionalTextShadow(_settings.TextOpacity * 0.6)
        });
    }

    private void AddListButton(DateTime date, string title, string detail, WpfBrush accent)
    {
        var titleText = new TextBlock
        {
            Text = title,
            Foreground = TextBrush(_settings.TextOpacity),
            FontWeight = FontWeights.SemiBold,
            FontSize = ScaledFont(FontScale.CardTitle, 13),
            Effect = OptionalTextShadow(_settings.TextOpacity)
        };

        var detailText = new TextBlock
        {
            Text = detail,
            Foreground = TextBrush(_settings.TextOpacity * 0.72),
            FontSize = ScaledFont(FontScale.Detail),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0),
            Effect = OptionalTextShadow(_settings.TextOpacity * 0.65)
        };

        var content = new DockPanel { LastChildFill = true };
        var accentBar = new Border
        {
            Background = accent,
            Width = 4,
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 0, 8, 0)
        };
        DockPanel.SetDock(accentBar, Dock.Left);
        content.Children.Add(accentBar);
        content.Children.Add(new StackPanel
        {
            Children = { titleText, detailText }
        });

        var button = new WpfButton
        {
            Tag = date,
            Content = content,
            HorizontalContentAlignment = WpfHorizontalAlignment.Stretch,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 6),
            Background = ListItemBrush,
            BorderBrush = ListItemBorderBrush,
            Cursor = WpfCursors.Hand
        };
        button.Click += ListDate_Click;
        ListContentPanel.Children.Add(button);
    }

    private void ListDate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: DateTime date })
        {
            OpenDayEditor(date);
        }
    }
}
