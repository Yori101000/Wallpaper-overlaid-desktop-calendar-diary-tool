using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TransparentCalendar.Models;
using TransparentCalendar.Services;
using TransparentCalendar.Views;

namespace TransparentCalendar;

public partial class MainWindow : Window
{
    private readonly StorageService _storage = new();
    private readonly Dictionary<string, CalendarEntry> _entries;
    private AppSettings _settings;
    private DateTime _visibleMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    public MainWindow()
    {
        InitializeComponent();

        _settings = _storage.LoadSettings();
        _entries = _storage.LoadEntries();
        ApplySettings();
        RenderCalendar();

        Loaded += (_, _) => BringIntoViewOnce();
        Closing += (_, _) => SaveWindowSettings();
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
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
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
    }

    private Button CreateDayButton(DateTime date)
    {
        var key = DateKey(date);
        var isCurrentMonth = date.Month == _visibleMonth.Month;
        var hasContent = _entries.TryGetValue(key, out var entry)
            && (entry.Todos.Any(todo => !string.IsNullOrWhiteSpace(todo.Text)) || !string.IsNullOrWhiteSpace(entry.Diary));

        var dayText = new TextBlock
        {
            Text = hasContent ? $"{date.Day} ·" : date.Day.ToString(CultureInfo.InvariantCulture),
            FontSize = _settings.FontSize,
            FontWeight = date.Date == DateTime.Today ? FontWeights.Normal : FontWeights.Light,
            Foreground = ParseBrush(_settings.TextColor, isCurrentMonth ? _settings.TextOpacity : _settings.TextOpacity * 0.35),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var button = new Button
        {
            Content = dayText,
            Tag = date,
            Style = (Style)FindResource("CalendarButtonStyle"),
            ToolTip = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };
        button.Click += DayButton_Click;
        return button;
    }

    private int GetFirstDayOffset(DateTime month)
    {
        var day = (int)month.DayOfWeek;
        return _settings.StartWithMonday ? (day + 6) % 7 : day;
    }

    private void DayButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DateTime date })
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
        SaveWindowSettings();
        var settingsWindow = new SettingsWindow(_settings, _storage)
        {
            Owner = this
        };

        if (settingsWindow.ShowDialog() == true)
        {
            _settings = settingsWindow.Settings;
            _storage.SaveSettings(_settings);
            ApplySettings();
            RenderCalendar();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
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

    private void SaveWindowSettings()
    {
        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Width = Width;
        _settings.Height = Height;
        _storage.SaveSettings(_settings);
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

    private static string DateKey(DateTime date)
    {
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static SolidColorBrush ParseBrush(string color, double opacity)
    {
        try
        {
            var parsed = (Color)ColorConverter.ConvertFromString(color);
            parsed.A = (byte)Math.Clamp(opacity * 255, 0, 255);
            return new SolidColorBrush(parsed);
        }
        catch
        {
            return new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(opacity * 255, 0, 255), 255, 255, 255));
        }
    }
}
