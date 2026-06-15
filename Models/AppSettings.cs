namespace TransparentCalendar.Models;

public sealed class AppSettings
{
    public double Left { get; set; } = 80;
    public double Top { get; set; } = 80;
    public double Width { get; set; } = 760;
    public double Height { get; set; } = 520;
    public double TextOpacity { get; set; } = 0.9;
    public double FontSize { get; set; } = 28;
    public string TextColor { get; set; } = "#FFFFFFFF";
    public bool IsLocked { get; set; }
    public bool StartWithMonday { get; set; } = true;
    public bool StartOnBoot { get; set; }
    public bool KeepOnTop { get; set; }
    public bool AttachToDesktopLayer { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool StartInTray { get; set; }
}
