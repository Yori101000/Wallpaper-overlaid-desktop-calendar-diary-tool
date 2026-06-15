using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
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
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;

namespace TransparentCalendar;

public partial class MainWindow : Window
{
    private static readonly SolidColorBrush TodoMarkerBrush = new(WpfColor.FromRgb(255, 209, 102));
    private static readonly SolidColorBrush DiaryMarkerBrush = new(WpfColor.FromRgb(123, 223, 242));
    private static readonly SolidColorBrush ImportantBadgeBrush = new(WpfColor.FromRgb(239, 71, 111));
    private static readonly SolidColorBrush TodoBadgeBrush = new(WpfColor.FromRgb(46, 196, 182));

    private readonly StorageService _storage = new();
    private readonly Dictionary<string, CalendarEntry> _entries;
    private Forms.NotifyIcon? _trayIcon;
    private AppSettings _settings;
    private DateTime _visibleMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private bool _isExitRequested;

    public MainWindow()
    {
        InitializeComponent();

        _settings = _storage.LoadSettings();
        _entries = _storage.LoadEntries();
        InitializeTrayIcon();
        CreateStartupBackup();
        ApplySettings();
        RenderCalendar();

        SourceInitialized += (_, _) => AttachToDesktopLayerIfEnabled();
        Loaded += (_, _) => ApplyStartupVisibility();
        Closing += MainWindow_Closing;
        Closed += (_, _) => DisposeTrayIcon();
    }

    private void ApplySettings()
    {
        NormalizeWindowBounds();
        Left = _settings.Left;
        Top = _settings.Top;
        Width = _settings.Width;
        Height = _settings.Height;
        ResizeMode = _settings.IsLocked ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
        Topmost = _settings.KeepOnTop;

        var brush = ParseBrush(_settings.TextColor, _settings.TextOpacity);
        Root.Opacity = _settings.TextOpacity;
        MonthTitle.Foreground = brush;
        MonthTitle.FontSize = _settings.FontSize * 0.95;
        TodayTodoTitle.Foreground = brush;
        TodayTodoTitle.FontSize = Math.Max(12, _settings.FontSize * 0.45);
        RenderWeekHeader();
    }

    private void NormalizeWindowBounds()
    {
        var minLeft = SystemParameters.VirtualScreenLeft;
        var minTop = SystemParameters.VirtualScreenTop;
        var maxLeft = minLeft + SystemParameters.VirtualScreenWidth - 120;
        var maxTop = minTop + SystemParameters.VirtualScreenHeight - 120;

        _settings.Width = Math.Clamp(_settings.Width, MinWidth, SystemParameters.VirtualScreenWidth);
        _settings.Height = Math.Clamp(_settings.Height, MinHeight, SystemParameters.VirtualScreenHeight);
        _settings.Left = Math.Clamp(_settings.Left, minLeft, maxLeft);
        _settings.Top = Math.Clamp(_settings.Top, minTop, maxTop);
    }

    private void RenderWeekHeader()
    {
        WeekHeaderGrid.Children.Clear();
        var days = _settings.StartWithMonday
            ? ["一", "二", "三", "四", "五", "六", "日"]
            : new[] { "日", "一", "二", "三", "四", "五", "六" };

        foreach (var day in days)
        {
            WeekHeaderGrid.Children.Add(new TextBlock
            {
                Text = day,
                Foreground = ParseBrush(_settings.TextColor, _settings.TextOpacity * 0.85),
                FontSize = Math.Max(12, _settings.FontSize * 0.45),
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = WpfVerticalAlignment.Center,
                FontWeight = FontWeights.Light
            });
        }
    }

    private void RenderCalendar()
    {
        MonthTitle.Text = _visibleMonth.ToString("yyyy 年 M 月", CultureInfo.GetCultureInfo("zh-CN"));
        MonthTitle.Foreground = ParseBrush(_settings.TextColor, _settings.TextOpacity);
        CalendarGrid.Children.Clear();

        var firstDayOffset = GetFirstDayOffset(_visibleMonth);
        var startDate = _visibleMonth.AddDays(-firstDayOffset);

        for (var i = 0; i < 42; i++)
        {
            var date = startDate.AddDays(i);
            CalendarGrid.Children.Add(CreateDayButton(date));
        }

        RenderTodayTodos();
    }

    private WpfButton CreateDayButton(DateTime date)
    {
        var key = DateKey(date);
        var isCurrentMonth = date.Month == _visibleMonth.Month;
        _entries.TryGetValue(key, out var entry);

        var todos = entry?.Todos
            .Where(todo => !string.IsNullOrWhiteSpace(todo.Text))
            .ToList() ?? [];
        var unfinishedCount = todos.Count(todo => !todo.IsDone);
        var hasImportantTodo = todos.Any(todo => IsImportantTodo(todo) && !todo.IsDone);
        var hasTodos = todos.Count > 0;
        var hasDiary = entry is not null && !string.IsNullOrWhiteSpace(entry.Diary);

        var button = new WpfButton
        {
            Content = CreateDayContent(date, isCurrentMonth, hasTodos, hasDiary, unfinishedCount, hasImportantTodo),
            Tag = date,
            Style = (Style)FindResource("CalendarButtonStyle"),
            HorizontalContentAlignment = WpfHorizontalAlignment.Stretch,
            VerticalContentAlignment = WpfVerticalAlignment.Stretch,
            ToolTip = BuildDayToolTip(date, hasTodos, hasDiary, unfinishedCount),
            Background = date.Date == DateTime.Today
                ? new SolidColorBrush(WpfColor.FromArgb(42, 255, 255, 255))
                : WpfBrushes.Transparent
        };
        button.Click += DayButton_Click;
        return button;
    }

    private Grid CreateDayContent(
        DateTime date,
        bool isCurrentMonth,
        bool hasTodos,
        bool hasDiary,
        int unfinishedCount,
        bool hasImportantTodo)
    {
        var opacity = isCurrentMonth ? _settings.TextOpacity : _settings.TextOpacity * 0.35;
        var foreground = ParseBrush(_settings.TextColor, opacity);
        var content = new Grid { Margin = new Thickness(2) };

        if (date.Date == DateTime.Today)
        {
            content.Children.Add(new Border
            {
                BorderBrush = ParseBrush(_settings.TextColor, Math.Min(1, _settings.TextOpacity * 0.75)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(2)
            });
        }

        content.Children.Add(new TextBlock
        {
            Text = date.Day.ToString(CultureInfo.InvariantCulture),
            FontSize = _settings.FontSize,
            FontWeight = date.Date == DateTime.Today ? FontWeights.Normal : FontWeights.Light,
            Foreground = foreground,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = WpfVerticalAlignment.Center
        });

        if (hasTodos || hasDiary)
        {
            var markerPanel = new StackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = WpfVerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 4)
            };

            if (hasTodos)
            {
                markerPanel.Children.Add(CreateMarker(hasImportantTodo ? ImportantBadgeBrush : TodoMarkerBrush));
            }

            if (hasDiary)
            {
                markerPanel.Children.Add(CreateMarker(DiaryMarkerBrush));
            }

            content.Children.Add(markerPanel);
        }

        if (unfinishedCount > 0)
        {
            var badgeText = unfinishedCount > 9 ? "9+" : unfinishedCount.ToString(CultureInfo.InvariantCulture);
            content.Children.Add(new Border
            {
                Background = hasImportantTodo ? ImportantBadgeBrush : TodoBadgeBrush,
                CornerRadius = new CornerRadius(8),
                MinWidth = 18,
                Height = 18,
                Padding = new Thickness(4, 0, 4, 0),
                HorizontalAlignment = WpfHorizontalAlignment.Right,
                VerticalAlignment = WpfVerticalAlignment.Top,
                Margin = new Thickness(0, 4, 4, 0),
                Child = new TextBlock
                {
                    Text = badgeText,
                    Foreground = WpfBrushes.White,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = WpfHorizontalAlignment.Center,
                    VerticalAlignment = WpfVerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                }
            });
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

    private static string BuildDayToolTip(DateTime date, bool hasTodos, bool hasDiary, int unfinishedCount)
    {
        var parts = new List<string> { date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };

        if (unfinishedCount > 0)
        {
            parts.Add($"未完成待办：{unfinishedCount}");
        }
        else if (hasTodos)
        {
            parts.Add("待办已完成");
        }

        if (hasDiary)
        {
            parts.Add("有日记");
        }

        return string.Join(Environment.NewLine, parts);
    }

    private void RenderTodayTodos()
    {
        TodayTodoItems.Children.Clear();
        var key = DateKey(DateTime.Today);
        if (!_entries.TryGetValue(key, out var entry))
        {
            TodayTodoPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var unfinishedTodos = entry.Todos
            .Where(todo => !todo.IsDone && !string.IsNullOrWhiteSpace(todo.Text))
            .ToList();

        if (unfinishedTodos.Count == 0)
        {
            TodayTodoPanel.Visibility = Visibility.Collapsed;
            return;
        }

        TodayTodoPanel.Visibility = Visibility.Visible;
        foreach (var todo in unfinishedTodos.Take(6))
        {
            var isImportant = IsImportantTodo(todo);
            TodayTodoItems.Children.Add(new TextBlock
            {
                Text = isImportant ? $"重要：{todo.Text.Trim()}" : todo.Text.Trim(),
                Foreground = isImportant ? ImportantBadgeBrush : ParseBrush(_settings.TextColor, _settings.TextOpacity),
                FontSize = Math.Max(12, _settings.FontSize * 0.45),
                Margin = new Thickness(0, 0, 12, 2),
                VerticalAlignment = WpfVerticalAlignment.Center
            });
        }

        if (unfinishedTodos.Count > 6)
        {
            TodayTodoItems.Children.Add(new TextBlock
            {
                Text = $"还有 {unfinishedTodos.Count - 6} 项",
                Foreground = ParseBrush(_settings.TextColor, _settings.TextOpacity * 0.8),
                FontSize = Math.Max(12, _settings.FontSize * 0.45),
                Margin = new Thickness(0, 0, 12, 2),
                VerticalAlignment = WpfVerticalAlignment.Center
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
        if (sender is not WpfButton { Tag: DateTime date })
        {
            return;
        }

        var key = DateKey(date);
        if (!_entries.TryGetValue(key, out var entry))
        {
            entry = new CalendarEntry { Date = key };
            _entries[key] = entry;
        }

        var editor = new DayEditorWindow(date, entry)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
        {
            entry.UpdatedAt = DateTime.Now;
            _storage.SaveEntries(_entries);
            RenderCalendar();
        }
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

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        OpenSettings();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.CloseToTray)
        {
            HideWindowToTray();
            return;
        }

        ExitApplication();
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_settings.IsLocked || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
            // DragMove can throw when the pointer is released during startup or modal transitions.
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        SaveWindowSettings();
        if (_isExitRequested)
        {
            return;
        }

        if (_settings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void OpenSettings()
    {
        SaveWindowSettings();
        if (!IsVisible)
        {
            ShowWindowFromTray();
        }

        var settingsWindow = new SettingsWindow(_settings, _storage, _entries)
        {
            Owner = this
        };

        var accepted = settingsWindow.ShowDialog() == true;
        if (accepted || settingsWindow.DataImported)
        {
            _settings = settingsWindow.Settings;
            if (accepted)
            {
                _storage.SaveSettings(_settings);
            }

            ApplySettings();
            RenderCalendar();
            AttachToDesktopLayerIfEnabled();
        }
    }

    private void SaveWindowSettings()
    {
        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Width = Width;
        _settings.Height = Height;
        _storage.SaveSettings(_settings);
    }

    private void ApplyStartupVisibility()
    {
        if (_settings.StartInTray)
        {
            HideWindowToTray();
            return;
        }

        BringIntoViewOnce();
    }

    private void BringIntoViewOnce()
    {
        if (_settings.KeepOnTop)
        {
            Activate();
            return;
        }

        Topmost = true;
        Activate();
        Topmost = false;
    }

    private void AttachToDesktopLayerIfEnabled()
    {
        if (_settings.AttachToDesktopLayer)
        {
            DesktopWindowService.AttachToDesktop(this);
        }
    }

    private void InitializeTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示", null, (_, _) => Dispatcher.Invoke(ShowWindowFromTray));
        menu.Items.Add("隐藏", null, (_, _) => Dispatcher.Invoke(HideWindowToTray));
        menu.Items.Add("设置", null, (_, _) => Dispatcher.Invoke(OpenSettings));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = Drawing.SystemIcons.Application,
            Text = "透明日历",
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.MouseDoubleClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
            {
                Dispatcher.Invoke(ToggleWindowVisibility);
            }
        };
    }

    private void ToggleWindowVisibility()
    {
        if (IsVisible)
        {
            HideWindowToTray();
            return;
        }

        ShowWindowFromTray();
    }

    private void ShowWindowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private void HideWindowToTray()
    {
        SaveWindowSettings();
        Hide();
    }

    private void ExitApplication()
    {
        _isExitRequested = true;
        Close();
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon is null)
        {
            return;
        }

        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayIcon = null;
    }

    private void CreateStartupBackup()
    {
        try
        {
            _storage.CreateAutomaticBackup(_settings, _entries);
        }
        catch
        {
            // Backup failures should not prevent the calendar from opening.
        }
    }

    private static bool IsImportantTodo(TodoItem todo)
    {
        return string.Equals(todo.Priority, "重要", StringComparison.Ordinal);
    }

    private static string DateKey(DateTime date)
    {
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static SolidColorBrush ParseBrush(string color, double opacity)
    {
        try
        {
            var parsed = (WpfColor)WpfColorConverter.ConvertFromString(color);
            parsed.A = (byte)Math.Clamp(opacity * 255, 0, 255);
            return new SolidColorBrush(parsed);
        }
        catch
        {
            return new SolidColorBrush(WpfColor.FromArgb((byte)Math.Clamp(opacity * 255, 0, 255), 255, 255, 255));
        }
    }
}
