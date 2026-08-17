using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TransparentCalendar.Models;
using TransparentCalendar.Services;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using WpfColor = System.Windows.Media.Color;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfMessageBox = System.Windows.MessageBox;

namespace TransparentCalendar.Views;

public partial class SettingsWindow : Window
{
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

        // 编辑副本，取消时不影响主窗口；Clone 避免逐字段手抄漏掉新增设置项。
        Settings = settings.Clone();
        Settings.StartOnBoot = _startup.IsEnabled();

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
        Settings.TextColor = string.IsNullOrWhiteSpace(ColorText.Text) ? "#FFFFFFFF" : ColorText.Text.Trim();
        Settings.IsLocked = LockWindowCheck.IsChecked == true;
        Settings.StartWithMonday = MondayStartCheck.IsChecked == true;
        Settings.ShowLunar = ShowLunarCheck.IsChecked == true;
        Settings.ShowStatutoryHolidays = ShowHolidayCheck.IsChecked == true;
        Settings.WindowLayer = GetSelectedWindowLayer();
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
            Log.Error("导出备份失败。", ex);
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
            // 覆盖现有数据前强制留一份备份（不受"当天已有备份则跳过"的限制）。
            _storage.CreateAutomaticBackup(Settings, _entries, force: true);
            var backup = _storage.LoadBackup(dialog.FileName);
            _storage.RestoreBackup(backup);

            Settings = backup.Settings;
            Settings.StartOnBoot = _startup.IsEnabled();
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
            Log.Error("导入备份失败。", ex);
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

    private void BgOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateSliderLabels();
    }

    /// <summary>选中预设时，同步套用它的文字颜色与不透明度（"高对比"因此才真正不同于"清晰白"）。</summary>
    private void ThemePresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingSettingsToControls)
        {
            return;
        }

        if (ThemePresets.Find(GetSelectedThemePreset()) is not { } preset)
        {
            return;
        }

        ColorText.Text = preset.TextColor;
        OpacitySlider.Value = preset.TextOpacity;
    }

    /// <summary>
    /// 复用 WinForms 的系统取色器 —— 本工程已经因为托盘图标引用了 WinForms，
    /// 不需要为此再写一个 WPF 取色控件。
    /// </summary>
    private void PickColor_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.ColorDialog { FullOpen = true, AnyColor = true };

        if (TryParseColor(ColorText.Text, out var current))
        {
            dialog.Color = Drawing.Color.FromArgb(current.A, current.R, current.G, current.B);
        }

        if (dialog.ShowDialog() != Forms.DialogResult.OK)
        {
            return;
        }

        // 系统取色器不返回 Alpha，保留当前的透明度分量。
        var alpha = TryParseColor(ColorText.Text, out var existing) ? existing.A : (byte)255;
        ColorText.Text = $"#{alpha:X2}{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
    }

    private void ColorText_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdateColorPreview();
    }

    /// <summary>色块实时反映输入；格式非法时给出红色斜杠提示，不再静默回退白色。</summary>
    private void UpdateColorPreview()
    {
        if (ColorPreview is null)
        {
            return;
        }

        if (TryParseColor(ColorText.Text, out var color))
        {
            ColorPreview.Background = new SolidColorBrush(color);
            ColorPreview.BorderBrush = new SolidColorBrush(WpfColor.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
            ColorPreview.ToolTip = null;
            return;
        }

        ColorPreview.Background = System.Windows.Media.Brushes.Transparent;
        ColorPreview.BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0xEF, 0x47, 0x6F));
        ColorPreview.ToolTip = "颜色格式无效，保存后会回退为白色。支持 #AARRGGBB / #RRGGBB / 颜色名。";
    }

    private static bool TryParseColor(string? text, out WpfColor color)
    {
        color = WpfColor.FromRgb(255, 255, 255);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            color = (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(text.Trim());
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void UpdateSliderLabels()
    {
        if (OpacityValue is null || FontSizeValue is null || BgOpacityValue is null)
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
            SelectWindowLayer(Settings.WindowLayer);
            LockWindowCheck.IsChecked = Settings.IsLocked;
            MondayStartCheck.IsChecked = Settings.StartWithMonday;
            ShowLunarCheck.IsChecked = Settings.ShowLunar;
            ShowHolidayCheck.IsChecked = Settings.ShowStatutoryHolidays;
            BootCheck.IsChecked = Settings.StartOnBoot;
            CloseToTrayCheck.IsChecked = Settings.CloseToTray;
            StartInTrayCheck.IsChecked = Settings.StartInTray;
            UpdateSliderLabels();
            UpdateColorPreview();
        }
        finally
        {
            _isApplyingSettingsToControls = false;
        }
    }

    private void SelectThemePreset(string preset, string textColor)
    {
        var matched = ThemePresets.Find(preset);
        var selectedPreset = matched is not null
            && string.Equals(matched.TextColor, textColor, StringComparison.OrdinalIgnoreCase)
                ? matched.Name
                : ThemePresets.Custom;

        SelectComboItem(ThemePresetCombo, selectedPreset);
    }

    private string GetSelectedThemePreset()
    {
        return GetSelectedComboItem(ThemePresetCombo) ?? ThemePresets.Custom;
    }

    private void SelectWindowLayer(string layer)
    {
        var selectedText = layer switch
        {
            WindowLayers.Bottom => "置底",
            WindowLayers.Desktop => "嵌入桌面",
            WindowLayers.Top => "置顶",
            _ => "普通"
        };

        SelectComboItem(WindowLayerCombo, selectedText);
    }

    private string GetSelectedWindowLayer()
    {
        return GetSelectedComboItem(WindowLayerCombo) switch
        {
            "置底" => WindowLayers.Bottom,
            "嵌入桌面" => WindowLayers.Desktop,
            "置顶" => WindowLayers.Top,
            _ => WindowLayers.Normal
        };
    }

    private static void SelectComboItem(WpfComboBox combo, string content)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), content, StringComparison.Ordinal))
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private static string? GetSelectedComboItem(WpfComboBox combo)
    {
        return combo.SelectedItem is ComboBoxItem item ? item.Content?.ToString() : null;
    }
}
