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
using TransparentCalendar.Models;
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
    private const int GwlExStyle = -20;
    private const int WsExAppWindow = 0x00040000;
    private const int WsExToolWindow = 0x00000080;
    private const int SwpNoSize = 0x0001;
    private const int SwpNoMove = 0x0002;
    private const int SwpNoZOrder = 0x0004;
    private const int SwpNoActivate = 0x0010;
    private const int SwpFrameChanged = 0x0020;
    private const string SidebarPositionRight = "Right";
    private const double SidebarWidth = 68;

    private static readonly SolidColorBrush TodoMarkerBrush = new(WpfColor.FromRgb(255, 209, 102));
    private static readonly SolidColorBrush DiaryMarkerBrush = new(WpfColor.FromRgb(123, 223, 242));
    private static readonly SolidColorBrush ImportantBadgeBrush = new(WpfColor.FromRgb(239, 71, 111));
    private static readonly SolidColorBrush TodoBadgeBrush = new(WpfColor.FromRgb(46, 196, 182));

    private readonly StorageService _storage = new();
    private readonly Dictionary<string, CalendarEntry> _entries;
    private Forms.NotifyIcon? _trayIcon;
    private AppSettings _settings;
    private DateTime _visibleMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _lastTodayDate = DateTime.Today;
    private bool _isExitRequested;
    private bool _isListMode;
    private bool _isNoteMode;
    private List<WebNoteGroup> _notes = [];
    private WebNoteGroup? _editingNoteGroup;
    private readonly NoteListenerService _noteListener;

    public MainWindow()
    {
        InitializeComponent();

        _settings = _storage.LoadSettings();
        _entries = _storage.LoadEntries();
        _notes = _storage.LoadWebNotes();
        InitializeTrayIcon();
        CreateStartupBackup();
        ApplySettings();
        RenderCalendar();
        StartDailyRefreshTimer();

        _noteListener = new NoteListenerService(_storage);
        _noteListener.OnNoteReceived += () => Dispatcher.Invoke(() => { _notes = _storage.LoadWebNotes(); if (_isNoteMode) RenderWebNotes(); });
        _noteListener.Start();

        SourceInitialized += (_, _) =>
        {
            HideMainWindowFromFastSwitcher();
            ApplyDesktopLayerSetting();
        };
        Loaded += (_, _) => ApplyStartupVisibility();
        Closing += MainWindow_Closing;
        Closed += (_, _) =>
        {
            DisposeTrayIcon();
        };
    }

    private void StartDailyRefreshTimer()
    {
        var timer = new System.Windows.Threading.DispatcherTimer();
        timer.Interval = TimeSpan.FromSeconds(60);
        timer.Tick += (_, _) =>
        {
            if (DateTime.Today != _lastTodayDate)
            {
                _lastTodayDate = DateTime.Today;
                RenderCalendar();
            }
        };
        timer.Start();
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
        Root.Opacity = 1;
        MonthTitle.Foreground = brush;
        MonthTitle.FontSize = _settings.FontSize * 0.95;
        MonthTitle.Effect = CreateTextShadow(_settings.TextOpacity);
        TodayTodoTitle.Foreground = brush;
        TodayTodoTitle.FontSize = Math.Max(12, _settings.FontSize * 0.45);
        TodayTodoTitle.Effect = CreateTextShadow(_settings.TextOpacity);
        // Apply background opacity to panels
        var bgAlpha = (byte)Math.Clamp(_settings.BackgroundOpacity * 255, 0, 255);
        var navBrush = ParseBrush(_settings.TextColor, _settings.TextOpacity);
        var bgColor = System.Windows.Media.Color.FromArgb(bgAlpha, 0x18, 0x18, 0x18);
        ModeSidebar.Background = new System.Windows.Media.SolidColorBrush(bgColor);
        MainContentPanel.Background = new System.Windows.Media.SolidColorBrush(bgColor);
        HeaderBar.Background = new System.Windows.Media.SolidColorBrush(bgColor);

        // Sync nav button foreground with text color
        if (PrevMonthBtn is not null)
        {
            PrevMonthBtn.Foreground = navBrush;
            NextMonthBtn.Foreground = navBrush;
            SettingsBtn.Foreground = navBrush;
            CloseBtn.Foreground = navBrush;
        }

        ApplySidebarPosition();
        UpdateModeButtons();
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
                FontWeight = FontWeights.Light,
                Effect = CreateTextShadow(_settings.TextOpacity * 0.7)
            });
        }
    }

    private void RenderCalendar()
    {
        MonthTitle.Text = _visibleMonth.ToString("yyyy 年 M 月", CultureInfo.GetCultureInfo("zh-CN"));
        MonthTitle.Foreground = ParseBrush(_settings.TextColor, _settings.TextOpacity);
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

        var firstDayOffset = GetFirstDayOffset(_visibleMonth);
        var startDate = _visibleMonth.AddDays(-firstDayOffset);

        for (var i = 0; i < 42; i++)
        {
            var date = startDate.AddDays(i);
            var button = CreateDayButton(date);
            Grid.SetRow(button, i / 7);
            Grid.SetColumn(button, i % 7);
            CalendarGrid.Children.Add(button);
        }

        RenderTodayTodos();
        RenderListView();
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
        var content = new Grid { Margin = new Thickness(1) };

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
            VerticalAlignment = WpfVerticalAlignment.Center,
            Effect = CreateTextShadow(opacity)
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
        var parts = new List<string> { date.ToString("yyyy-MM-dd dddd") };
        if (hasTodos) parts.Add($"待办 {unfinishedCount} 项未完成");
        if (hasDiary) parts.Add("含日记");
        return string.Join(" · ", parts);
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
                Text = BuildTodayTodoText(todo, isImportant),
                Foreground = isImportant ? ImportantBadgeBrush : ParseBrush(_settings.TextColor, _settings.TextOpacity),
                FontSize = Math.Max(12, _settings.FontSize * 0.45),
                Margin = new Thickness(0, 0, 12, 2),
                VerticalAlignment = WpfVerticalAlignment.Center,
                Effect = CreateTextShadow(_settings.TextOpacity * 0.7)
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
                VerticalAlignment = WpfVerticalAlignment.Center,
                Effect = CreateTextShadow(_settings.TextOpacity * 0.7)
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

        OpenDayEditor(date);
    }

    private void OpenDayEditor(DateTime date)
    {
        var key = DateKey(date);
        if (!_entries.TryGetValue(key, out var entry))
        {
            entry = new CalendarEntry { Date = key };
            _entries[key] = entry;
        }

        var editor = new DayEditorWindow(date, entry, PostponeTodoToDate)
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

    private void CalendarMode_Click(object sender, RoutedEventArgs e)
    {
        _isListMode = false;
        _isNoteMode = false;
        CalendarViewPanel.Visibility = Visibility.Visible;
        ListViewPanel.Visibility = Visibility.Collapsed;
        WebNoteViewPanel.Visibility = Visibility.Collapsed;
        UpdateModeButtons();
    }

    private void NoteMode_Click(object sender, RoutedEventArgs e)
    {
        SetNoteMode(true);
    }

    private void ListMode_Click(object sender, RoutedEventArgs e)
    {
        SetListMode(true);
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(SearchTextBox.Text))
        {
            SetListMode(true);
            return;
        }

        // 搜索清空：恢复到月历模式（搜索时自动切到列表，清空自然回退）
        CalendarMode_Click(sender, new RoutedEventArgs());
    }

    private void SetListMode(bool isListMode)
    {
        _isListMode = isListMode;
        if (isListMode) _isNoteMode = false;
        CalendarViewPanel.Visibility = _isListMode ? Visibility.Collapsed : Visibility.Visible;
        ListViewPanel.Visibility = _isListMode ? Visibility.Visible : Visibility.Collapsed;
        WebNoteViewPanel.Visibility = Visibility.Collapsed;
        UpdateModeButtons();
        RenderListView();
    }

    private void SetNoteMode(bool isNoteMode)
    {
        _isNoteMode = isNoteMode;
        if (isNoteMode) _isListMode = false;
        CalendarViewPanel.Visibility = _isNoteMode ? Visibility.Collapsed : Visibility.Visible;
        ListViewPanel.Visibility = Visibility.Collapsed;
        WebNoteViewPanel.Visibility = _isNoteMode ? Visibility.Visible : Visibility.Collapsed;
        UpdateModeButtons();
        if (isNoteMode) RenderWebNotes();
    }

    private void ApplySidebarPosition()
    {
        var isRight = string.Equals(_settings.SidebarPosition, SidebarPositionRight, StringComparison.Ordinal);
        LeftSidebarColumn.Width = isRight ? new GridLength(0) : new GridLength(SidebarWidth);
        RightSidebarColumn.Width = isRight ? new GridLength(SidebarWidth) : new GridLength(0);
        Grid.SetColumn(ModeSidebar, isRight ? 2 : 0);
        ModeSidebar.Margin = isRight ? new Thickness(10, 0, 0, 0) : new Thickness(0, 0, 10, 0);
    }

    private void UpdateModeButtons()
    {
        SetModeButtonSelected(CalendarModeButton, !_isListMode && !_isNoteMode);
        SetModeButtonSelected(ListModeButton, _isListMode);
        SetModeButtonSelected(NoteModeButton, _isNoteMode);
    }

    private static void SetModeButtonSelected(WpfButton button, bool isSelected)
    {
        button.Opacity = isSelected ? 1 : 0.72;
        button.FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal;
        button.Background = new SolidColorBrush(WpfColor.FromArgb(isSelected ? (byte)70 : (byte)24, 255, 255, 255));
        button.BorderBrush = new SolidColorBrush(WpfColor.FromArgb(isSelected ? (byte)95 : (byte)42, 255, 255, 255));
    }

    private void RenderListView()
    {
        if (ListContentPanel is null)
        {
            return;
        }

        ListContentPanel.Children.Clear();
        var searchText = SearchTextBox?.Text.Trim() ?? string.Empty;

        AddUnfinishedTodoSection(searchText);
        AddMonthRecordSection(searchText);
        AddRecentRecordSection(searchText);
    }

    private void AddUnfinishedTodoSection(string searchText)
    {
        AddListSectionTitle("未完成待办");
        var items = _entries
            .SelectMany(entry => entry.Value.Todos
                .Where(todo => !todo.IsDone && !string.IsNullOrWhiteSpace(todo.Text))
                .Select(todo => new { Date = ParseDateKey(entry.Key), Todo = todo }))
            .Where(item => item.Date is not null && TodoMatchesSearch(item.Todo, searchText))
            .OrderBy(item => item.Date)
            .Take(30)
            .ToList();

        if (items.Count == 0)
        {
            AddListEmptyText("没有未完成待办。");
            return;
        }

        foreach (var item in items)
        {
            var date = item.Date!.Value;
            AddListButton(
                date,
                date.ToString("yyyy-MM-dd dddd", CultureInfo.GetCultureInfo("zh-CN")),
                BuildTodoSummary(item.Todo),
                IsImportantTodo(item.Todo) ? ImportantBadgeBrush : TodoBadgeBrush);
        }
    }

    private void AddMonthRecordSection(string searchText)
    {
        AddListSectionTitle("本月记录");
        var items = _entries
            .Select(entry => new { Date = ParseDateKey(entry.Key), Entry = entry.Value })
            .Where(item => item.Date is not null
                && item.Date.Value.Year == _visibleMonth.Year
                && item.Date.Value.Month == _visibleMonth.Month
                && EntryHasContent(item.Entry)
                && EntryMatchesSearch(item.Entry, searchText))
            .OrderBy(item => item.Date)
            .ToList();

        if (items.Count == 0)
        {
            AddListEmptyText("本月没有记录。");
            return;
        }

        foreach (var item in items)
        {
            var date = item.Date!.Value;
            AddListButton(
                date,
                date.ToString("M 月 d 日 dddd", CultureInfo.GetCultureInfo("zh-CN")),
                BuildEntrySummary(item.Entry),
                DiaryMarkerBrush);
        }
    }

    private void AddRecentRecordSection(string searchText)
    {
        AddListSectionTitle("最近更新");
        var items = _entries
            .Select(entry => new { Date = ParseDateKey(entry.Key), Entry = entry.Value })
            .Where(item => item.Date is not null && EntryHasContent(item.Entry) && EntryMatchesSearch(item.Entry, searchText))
            .OrderByDescending(item => item.Entry.UpdatedAt)
            .Take(12)
            .ToList();

        if (items.Count == 0)
        {
            AddListEmptyText("没有最近记录。");
            return;
        }

        foreach (var item in items)
        {
            var date = item.Date!.Value;
            AddListButton(
                date,
                $"{date:yyyy-MM-dd}  更新于 {item.Entry.UpdatedAt:MM-dd HH:mm}",
                BuildEntrySummary(item.Entry),
                TodoMarkerBrush);
        }
    }

    private void AddListSectionTitle(string text)
    {
        ListContentPanel.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = ParseBrush(_settings.TextColor, _settings.TextOpacity),
            FontSize = Math.Max(14, _settings.FontSize * 0.52),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 6),
            Effect = CreateTextShadow(_settings.TextOpacity)
        });
    }

    private void AddListEmptyText(string text)
    {
        ListContentPanel.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = ParseBrush(_settings.TextColor, _settings.TextOpacity * 0.55),
            FontSize = Math.Max(12, _settings.FontSize * 0.42),
            Margin = new Thickness(0, 0, 0, 10),
            Effect = CreateTextShadow(_settings.TextOpacity * 0.6)
        });
    }

    private void AddListButton(DateTime date, string title, string detail, WpfBrush accent)
    {
        var titleText = new TextBlock
        {
            Text = title,
            Foreground = ParseBrush(_settings.TextColor, _settings.TextOpacity),
            FontWeight = FontWeights.SemiBold,
            FontSize = Math.Max(13, _settings.FontSize * 0.48),
            Effect = CreateTextShadow(_settings.TextOpacity)
        };

        var detailText = new TextBlock
        {
            Text = detail,
            Foreground = ParseBrush(_settings.TextColor, _settings.TextOpacity * 0.72),
            FontSize = Math.Max(12, _settings.FontSize * 0.4),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0),
            Effect = CreateTextShadow(_settings.TextOpacity * 0.65)
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
            Children =
            {
                titleText,
                detailText
            }
        });

        var button = new WpfButton
        {
            Tag = date,
            Content = content,
            HorizontalContentAlignment = WpfHorizontalAlignment.Stretch,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 6),
            Background = new SolidColorBrush(WpfColor.FromArgb(18, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(WpfColor.FromArgb(36, 255, 255, 255)),
            Cursor = System.Windows.Input.Cursors.Hand
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

    private void PostponeTodoToDate(DateTime targetDate, TodoItem todo)
    {
        var targetKey = DateKey(targetDate);
        if (!_entries.TryGetValue(targetKey, out var targetEntry))
        {
            targetEntry = new CalendarEntry { Date = targetKey };
            _entries[targetKey] = targetEntry;
        }

        targetEntry.Todos.Add(todo);
        targetEntry.UpdatedAt = DateTime.Now;
        _storage.SaveEntries(_entries);
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
        if (_settings.IsLocked || e.ChangedButton != MouseButton.Left || IsInteractiveDragSource(e.OriginalSource))
        {
            return;
        }

        TryDragMove();
    }

    private void TryDragMove()
    {
        try
        {
            DragMove();
        }
        catch
        {
            // DragMove can throw when the pointer is released during startup or modal transitions.
        }
    }

    private static bool IsInteractiveDragSource(object source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is WpfButton
                or System.Windows.Controls.TextBox
                or System.Windows.Controls.CheckBox
                or System.Windows.Controls.ComboBox
                or System.Windows.Controls.Primitives.ScrollBar
                or Slider)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
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

            ApplyDesktopLayerSetting();
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

    private void ApplyDesktopLayerSetting()
    {
        // Wallpaper Engine owns the desktop wallpaper layer. Do not SetParent this
        // window to WorkerW/Progman; keep it as a normal transparent overlay window.
        HideMainWindowFromFastSwitcher();
        Topmost = _settings.KeepOnTop;
    }

    private void HideMainWindowFromFastSwitcher()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var style = GetWindowLongPtrSafe(hwnd, GwlExStyle).ToInt64();
        style &= ~WsExAppWindow;
        style |= WsExToolWindow;
        SetWindowLongPtrSafe(hwnd, GwlExStyle, new IntPtr(style));
        SetWindowPos(
            hwnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
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
        HideMainWindowFromFastSwitcher();
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

    private static DateTime? ParseDateKey(string key)
    {
        return DateTime.TryParseExact(
            key,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date
            : null;
    }

    private static bool EntryHasContent(CalendarEntry entry)
    {
        return entry.Todos.Any(todo => !string.IsNullOrWhiteSpace(todo.Text))
            || !string.IsNullOrWhiteSpace(entry.Diary);
    }

    private static bool EntryMatchesSearch(CalendarEntry entry, string searchText)
    {
        return string.IsNullOrWhiteSpace(searchText)
            || (!string.IsNullOrWhiteSpace(entry.Diary)
                && entry.Diary.Contains(searchText, StringComparison.CurrentCultureIgnoreCase))
            || entry.Todos.Any(todo => TodoMatchesSearch(todo, searchText));
    }

    private static bool TodoMatchesSearch(TodoItem todo, string searchText)
    {
        return string.IsNullOrWhiteSpace(searchText)
            || todo.Text.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)
            || todo.Priority.Contains(searchText, StringComparison.CurrentCultureIgnoreCase);
    }

    private static string BuildTodoSummary(TodoItem todo)
    {
        var priority = IsImportantTodo(todo) ? "重要" : "普通";
        var postponed = string.IsNullOrWhiteSpace(todo.PostponedLabel) ? string.Empty : $" · {todo.PostponedLabel}";
        return $"{priority} · {todo.Text.Trim()}{postponed}";
    }

    private static string BuildEntrySummary(CalendarEntry entry)
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

    private static string PreviewText(string text)
    {
        var normalized = string.Join(
            " ",
            text.Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 42 ? normalized : $"{normalized[..42]}...";
    }

    private static string BuildTodayTodoText(TodoItem todo, bool isImportant)
    {
        var prefix = isImportant ? "重要：" : string.Empty;
        var postponed = string.IsNullOrWhiteSpace(todo.PostponedLabel) ? string.Empty : $"（{todo.PostponedLabel}）";
        return $"{prefix}{todo.Text.Trim()}{postponed}";
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

    private static DropShadowEffect CreateTextShadow(double opacity)
    {
        return new DropShadowEffect
        {
            Color = System.Windows.Media.Colors.Black,
            BlurRadius = 5,
            ShadowDepth = 0,
            Opacity = Math.Clamp(opacity * 0.65, 0.2, 0.65)
        };
    }

    private static IntPtr GetWindowLongPtrSafe(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr(hWnd, nIndex)
            : new IntPtr(GetWindowLong(hWnd, nIndex));
    }

    private static IntPtr SetWindowLongPtrSafe(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr(hWnd, nIndex, dwNewLong)
            : new IntPtr(SetWindowLong(hWnd, nIndex, dwNewLong.ToInt32()));
    }

    private void RenderWebNotes()
    {
        WebNoteListPanel.Children.Clear();
        _notes = _storage.LoadWebNotes();

        // Update bookmarklet code display
        var port = _noteListener?.Port ?? 51999;
        var code = $"javascript:(function(){{var t=window.getSelection()?.toString()||'';var u=location.href;var n=document.title;var x=new XMLHttpRequest();x.open('POST','http://localhost:{port}/save',true);x.setRequestHeader('Content-Type','application/json');x.send(JSON.stringify({{url:u,title:n,text:t}}));}})();";
        BookmarkletText.Text = code;

        if (_notes.Count == 0)
        {
            WebNoteListPanel.Children.Add(new TextBlock
            {
                Text = "暂无笔记，点击右侧 + 添加 添加网页笔记。",
                Foreground = ParseBrush(_settings.TextColor, _settings.TextOpacity * 0.55),
                FontSize = Math.Max(13, _settings.FontSize * 0.42),
                Margin = new Thickness(0, 20, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        foreach (var note in _notes.OrderByDescending(n => n.UpdatedAt))
        {
            var panel = new Border
            {
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(10),
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(WpfColor.FromArgb(18, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(WpfColor.FromArgb(30, 255, 255, 255)),
                BorderThickness = new Thickness(1)
            };

            var innerStack = new StackPanel();

            var titleBtn = new WpfButton
            {
                Content = note.Title,
                Tag = note,
                HorizontalContentAlignment = WpfHorizontalAlignment.Left,
                Foreground = ParseBrush(_settings.TextColor, _settings.TextOpacity),
                FontWeight = FontWeights.SemiBold,
                FontSize = Math.Max(14, _settings.FontSize * 0.48),
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = WpfBrushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0)
            };
            titleBtn.Click += NoteTitle_Click;
            innerStack.Children.Add(titleBtn);

            if (!string.IsNullOrWhiteSpace(note.Url))
            {
                var urlText = new TextBlock
                {
                    Text = note.Url,
                    Foreground = ParseBrush(_settings.TextColor, _settings.TextOpacity * 0.55),
                    FontSize = Math.Max(11, _settings.FontSize * 0.35),
                    Margin = new Thickness(0, 2, 0, 4),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                innerStack.Children.Add(urlText);
            }

            if (note.Notes.Count > 0)
            {
                var preview = string.Join(" ", note.Notes); if (preview.Length > 80) preview = preview[..80] + "...";
                innerStack.Children.Add(new TextBlock
                {
                    Text = preview,
                    Foreground = ParseBrush(_settings.TextColor, _settings.TextOpacity * 0.72),
                    FontSize = Math.Max(12, _settings.FontSize * 0.4),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 6)
                });
            }

            var actionBar = new StackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = WpfHorizontalAlignment.Right };
            var editBtn = new WpfButton
            {
                Content = "编辑",
                Tag = note,
                MinWidth = 40,
                Height = 24,
                Margin = new Thickness(0, 0, 6, 0),
                FontSize = 12,
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = new SolidColorBrush(WpfColor.FromArgb(24, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(WpfColor.FromArgb(40, 255, 255, 255)),
                Padding = new Thickness(4, 0, 4, 0)
            };
            editBtn.Click += EditNote_Click;
            actionBar.Children.Add(editBtn);

            var delBtn = new WpfButton
            {
                Content = "删除",
                Tag = note,
                MinWidth = 40,
                Height = 24,
                FontSize = 12,
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = new SolidColorBrush(WpfColor.FromArgb(24, 239, 71, 111)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(WpfColor.FromArgb(60, 239, 71, 111)),
                Padding = new Thickness(4, 0, 4, 0)
            };
            delBtn.Click += DeleteNote_Click;
            actionBar.Children.Add(delBtn);

            innerStack.Children.Add(actionBar);
            panel.Child = innerStack;
            WebNoteListPanel.Children.Add(panel);
        }
    }

    private void NoteTitle_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: WebNoteGroup note } && !string.IsNullOrWhiteSpace(note.Url))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = note.Url,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    private void AddNote_Click(object? sender, RoutedEventArgs e)
    {
        _editingNoteGroup = null;
        NoteTitleInput.Text = "";
        NoteUrlInput.Text = "";
        NoteContentInput.Text = "";



        NoteEditorPanel.Visibility = Visibility.Visible;
        NoteTitleInput.Focus();
    }

    private void EditNote_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: WebNoteGroup group })
        {
            _editingNoteGroup = group;
            NoteTitleInput.Text = group.Title;
            NoteUrlInput.Text = group.Url;
            NoteContentInput.Text = string.Join("\n", group.Notes);
            NoteEditorPanel.Visibility = Visibility.Visible;
            NoteTitleInput.Focus();
        }
    }

    private void NoteEditorSave_Click(object? sender, RoutedEventArgs e)
    {
        var title = (NoteTitleInput.Text ?? "").Trim();
        var url = (NoteUrlInput.Text ?? "").Trim();
        var notes = (NoteContentInput.Text ?? "").Trim();

        if (string.IsNullOrWhiteSpace(url))
        {
            System.Windows.MessageBox.Show("请输入网址。", "提示");
            return;
        }

        if (_editingNoteGroup is not null)
        {
            _editingNoteGroup.Title = string.IsNullOrWhiteSpace(title) ? url : title;
            _editingNoteGroup.Url = url;
            _editingNoteGroup.Notes.Clear(); if (!string.IsNullOrWhiteSpace(notes)) _editingNoteGroup.Notes.Add(notes);
            _editingNoteGroup.UpdatedAt = DateTime.Now;
        }
        else
        {
            _notes.Add(new WebNoteGroup
            {
                Id = Guid.NewGuid().ToString(),
                Title = string.IsNullOrWhiteSpace(title) ? url : title,
                Url = url,
                Notes = string.IsNullOrWhiteSpace(notes) ? [] : new System.Collections.Generic.List<string> { notes },
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            });
        }

        _editingNoteGroup = null;
        _storage.SaveWebNotes(_notes);
        NoteEditorPanel.Visibility = Visibility.Collapsed;
        RenderWebNotes();
    }

    private void NoteEditorCancel_Click(object? sender, RoutedEventArgs e)
    {
        _editingNoteGroup = null;
        NoteEditorPanel.Visibility = Visibility.Collapsed;
    }

    private void DeleteNote_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: WebNoteGroup group })
        {
            _notes.Remove(group);
            _storage.SaveWebNotes(_notes);
            RenderWebNotes();
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        int uFlags);
}






