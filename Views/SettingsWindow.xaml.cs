using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using TransparentCalendar.Models;
using TransparentCalendar.Services;
using WpfMessageBox = System.Windows.MessageBox;

namespace TransparentCalendar.Views;

public partial class SettingsWindow : Window
{
    private const string SidebarPositionLeft = "Left";
    private const string SidebarPositionRight = "Right";

    private static readonly Dictionary<string, string> ThemeTextColors = new(StringComparer.Ordinal)
    {
        ["清晰白"] = "#FFFFFFFF",
        ["柔和青"] = "#FF7BDFF2",
        ["暖金"] = "#FFFFD166",
        ["高对比"] = "#FFFFFFFF"
    };

    private readonly StorageService _storage;
    private readonly StartupService _startup = new();
    private readonly Dictionary<string, CalendarEntry> _entries;
    private bool _isApplyingSettingsToControls;

    public AppSettings Settings { get; private set; }
    public bool DataImported { get; private set; }

    public SettingsWindow(AppSettings settings, StorageService storage, Dictionary<string, CalendarEntry> entries)
    {
        InitializeComponent();
        _storage = storage;
        _entries = entries;
        Settings = new AppSettings
        {
            Left = settings.Left,
            Top = settings.Top,
            Width = settings.Width,
            Height = settings.Height,
            TextOpacity = settings.TextOpacity,
            FontSize = settings.FontSize,
            ThemePreset = settings.ThemePreset,
            SidebarPosition = settings.SidebarPosition,
            TextColor = settings.TextColor,
            IsLocked = settings.IsLocked,
            StartWithMonday = settings.StartWithMonday,
            KeepOnTop = settings.KeepOnTop,
            AttachToDesktopLayer = settings.AttachToDesktopLayer,
            CloseToTray = settings.CloseToTray,
            StartInTray = settings.StartInTray,
            BackgroundOpacity = settings.BackgroundOpacity,
            StartOnBoot = _startup.IsEnabled()
        };

        ApplySettingsToControls();
        StoragePathText.Text = _storage.AppDataDirectory;
    }

    private void OpenStorage_Click(object sender, RoutedEventArgs e)
    {
        _storage.EnsureDirectory();
        Process.Start(new ProcessStartInfo
        {
            FileName = _storage.AppDataDirectory,
            UseShellExecute = true
        });
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left
            || e.OriginalSource is System.Windows.Controls.Button)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
            // 拖动过程中释放鼠标可能抛出异常，不影响窗口状态。
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Settings.BackgroundOpacity = BgOpacitySlider.Value;
            Settings.TextOpacity = OpacitySlider.Value;
        Settings.FontSize = FontSizeSlider.Value;
        Settings.ThemePreset = GetSelectedThemePreset();
        Settings.SidebarPosition = GetSelectedSidebarPosition();
        Settings.TextColor = string.IsNullOrWhiteSpace(ColorText.Text) ? "#FFFFFFFF" : ColorText.Text.Trim();
        Settings.IsLocked = LockWindowCheck.IsChecked == true;
        Settings.StartWithMonday = MondayStartCheck.IsChecked == true;
        Settings.KeepOnTop = TopmostCheck.IsChecked == true;
        Settings.AttachToDesktopLayer = DesktopLayerCheck.IsChecked == true;
        Settings.StartOnBoot = BootCheck.IsChecked == true;
        Settings.CloseToTray = CloseToTrayCheck.IsChecked == true;
        Settings.StartInTray = StartInTrayCheck.IsChecked == true;
        _startup.SetEnabled(Settings.StartOnBoot);
        DialogResult = true;
    }

    private void ExportBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"透明日历备份-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            Filter = "JSON 备份文件 (*.json)|*.json|所有文件 (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _storage.ExportBackup(dialog.FileName, Settings, _entries);
            WpfMessageBox.Show(this, "备份已导出。", "透明日历", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(this, $"导出备份失败：{ex.Message}", "透明日历", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "JSON 备份文件 (*.json)|*.json|所有文件 (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _storage.CreateAutomaticBackup(Settings, _entries);
            var backup = _storage.LoadBackup(dialog.FileName);
            _storage.RestoreBackup(backup);

            Settings = backup.Settings;
            _entries.Clear();
            foreach (var entry in backup.Entries)
            {
                _entries[entry.Key] = entry.Value;
            }

            ApplySettingsToControls();
            DataImported = true;
            WpfMessageBox.Show(this, "备份已导入。", "透明日历", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(this, $"导入备份失败：{ex.Message}", "透明日历", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateSliderLabels();
    }

    private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateSliderLabels();
    }

    private void ThemePresetCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isApplyingSettingsToControls)
        {
            return;
        }

        var selectedPreset = GetSelectedThemePreset();
        if (ThemeTextColors.TryGetValue(selectedPreset, out var textColor))
        {
            ColorText.Text = textColor;
        }
    }

    private void BgOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BgOpacityValue is null) return;
        BgOpacityValue.Text = BgOpacitySlider.Value.ToString("P0", CultureInfo.GetCultureInfo("zh-CN"));
    }

    private void UpdateSliderLabels()
    {
        if (OpacityValue is null || FontSizeValue is null)
        {
            return;
        }

        BgOpacityValue.Text = BgOpacitySlider.Value.ToString("P0", CultureInfo.GetCultureInfo("zh-CN"));
            OpacityValue.Text = OpacitySlider.Value.ToString("P0", CultureInfo.GetCultureInfo("zh-CN"));
        FontSizeValue.Text = FontSizeSlider.Value.ToString("F0", CultureInfo.InvariantCulture);
    }

    private void ApplySettingsToControls()
    {
        _isApplyingSettingsToControls = true;
        try
        {
            BgOpacitySlider.Value = Settings.BackgroundOpacity;
            OpacitySlider.Value = Settings.TextOpacity;
            FontSizeSlider.Value = Settings.FontSize;
            ColorText.Text = Settings.TextColor;
            SelectThemePreset(Settings.ThemePreset, Settings.TextColor);
            SelectSidebarPosition(Settings.SidebarPosition);
            LockWindowCheck.IsChecked = Settings.IsLocked;
            MondayStartCheck.IsChecked = Settings.StartWithMonday;
            TopmostCheck.IsChecked = Settings.KeepOnTop;
            DesktopLayerCheck.IsChecked = Settings.AttachToDesktopLayer;
            BootCheck.IsChecked = Settings.StartOnBoot;
            CloseToTrayCheck.IsChecked = Settings.CloseToTray;
            StartInTrayCheck.IsChecked = Settings.StartInTray;
            UpdateSliderLabels();
        }
        finally
        {
            _isApplyingSettingsToControls = false;
        }
    }

    private void SelectThemePreset(string preset, string textColor)
    {
        var selectedPreset = ThemeTextColors.TryGetValue(preset, out var presetColor)
            && string.Equals(presetColor, textColor, StringComparison.OrdinalIgnoreCase)
            ? preset
            : "自定义";

        foreach (var item in ThemePresetCombo.Items.OfType<System.Windows.Controls.ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), selectedPreset, StringComparison.Ordinal))
            {
                ThemePresetCombo.SelectedItem = item;
                return;
            }
        }
    }

    private string GetSelectedThemePreset()
    {
        return ThemePresetCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item
            ? item.Content?.ToString() ?? "自定义"
            : "自定义";
    }

    private void SelectSidebarPosition(string position)
    {
        var selectedText = string.Equals(position, SidebarPositionRight, StringComparison.Ordinal)
            ? "右侧"
            : "左侧";

        foreach (var item in SidebarPositionCombo.Items.OfType<System.Windows.Controls.ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), selectedText, StringComparison.Ordinal))
            {
                SidebarPositionCombo.SelectedItem = item;
                return;
            }
        }
    }

    private string GetSelectedSidebarPosition()
    {
        if (SidebarPositionCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item
            && string.Equals(item.Content?.ToString(), "右侧", StringComparison.Ordinal))
        {
            return SidebarPositionRight;
        }

        return SidebarPositionLeft;
    }
}
