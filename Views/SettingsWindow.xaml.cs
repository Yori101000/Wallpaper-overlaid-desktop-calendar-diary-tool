using System.Diagnostics;
using System.Globalization;
using System.Windows;
using TransparentCalendar.Models;
using TransparentCalendar.Services;

namespace TransparentCalendar.Views;

public partial class SettingsWindow : Window
{
    private readonly StorageService _storage;
    private readonly StartupService _startup = new();

    public AppSettings Settings { get; private set; }

    public SettingsWindow(AppSettings settings, StorageService storage)
    {
        InitializeComponent();
        _storage = storage;
        Settings = new AppSettings
        {
            Left = settings.Left,
            Top = settings.Top,
            Width = settings.Width,
            Height = settings.Height,
            TextOpacity = settings.TextOpacity,
            FontSize = settings.FontSize,
            TextColor = settings.TextColor,
            IsLocked = settings.IsLocked,
            StartWithMonday = settings.StartWithMonday,
            KeepOnTop = settings.KeepOnTop,
            AttachToDesktopLayer = settings.AttachToDesktopLayer,
            StartOnBoot = _startup.IsEnabled()
        };

        OpacitySlider.Value = Settings.TextOpacity;
        FontSizeSlider.Value = Settings.FontSize;
        ColorText.Text = Settings.TextColor;
        LockWindowCheck.IsChecked = Settings.IsLocked;
        MondayStartCheck.IsChecked = Settings.StartWithMonday;
        TopmostCheck.IsChecked = Settings.KeepOnTop;
        BootCheck.IsChecked = Settings.StartOnBoot;
        StoragePathText.Text = _storage.AppDataDirectory;
        UpdateSliderLabels();
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

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Settings.TextOpacity = OpacitySlider.Value;
        Settings.FontSize = FontSizeSlider.Value;
        Settings.TextColor = string.IsNullOrWhiteSpace(ColorText.Text) ? "#FFFFFFFF" : ColorText.Text.Trim();
        Settings.IsLocked = LockWindowCheck.IsChecked == true;
        Settings.StartWithMonday = MondayStartCheck.IsChecked == true;
        Settings.KeepOnTop = TopmostCheck.IsChecked == true;
        Settings.StartOnBoot = BootCheck.IsChecked == true;
        _startup.SetEnabled(Settings.StartOnBoot);
        DialogResult = true;
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

    private void UpdateSliderLabels()
    {
        if (OpacityValue is null || FontSizeValue is null)
        {
            return;
        }

        OpacityValue.Text = OpacitySlider.Value.ToString("P0", CultureInfo.GetCultureInfo("zh-CN"));
        FontSizeValue.Text = FontSizeSlider.Value.ToString("F0", CultureInfo.InvariantCulture);
    }
}
