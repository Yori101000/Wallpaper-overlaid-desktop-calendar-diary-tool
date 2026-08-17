namespace TransparentCalendar.Models;

/// <summary>
/// 主题预设：文字颜色、文字不透明度、阴影强度、面板底色（RGB，透明度由
/// <c>AppSettings.BackgroundOpacity</c> 单独控制）。
/// </summary>
public sealed record ThemePreset(
    string Name,
    string TextColor,
    double TextOpacity,
    double ShadowStrength,
    byte PanelR,
    byte PanelG,
    byte PanelB);

public static class ThemePresets
{
    public const string Custom = "自定义";

    /// <summary>未命中任何预设时的面板底色（沿用历史默认的 #181818）。</summary>
    public const byte DefaultPanelR = 0x18;
    public const byte DefaultPanelG = 0x18;
    public const byte DefaultPanelB = 0x18;

    public static readonly IReadOnlyList<ThemePreset> All =
    [
        new("清晰白", "#FFFFFFFF", 0.90, 1.0, 0x18, 0x18, 0x18),
        // 冷色文字配偏蓝的深底
        new("柔和青", "#FF7BDFF2", 0.88, 1.0, 0x10, 0x1A, 0x20),
        // 暖色文字配偏褐的深底
        new("暖金", "#FFFFD166", 0.90, 1.0, 0x1E, 0x18, 0x10),
        // 高对比：满不透明度 + 更重的描边阴影 + 纯黑底，用于浅色/花哨壁纸。
        new("高对比", "#FFFFFFFF", 1.00, 2.0, 0x00, 0x00, 0x00)
    ];

    /// <summary>取预设的面板底色；无匹配预设（自定义）时用默认深灰。</summary>
    public static (byte R, byte G, byte B) PanelColorFor(string? presetName)
    {
        return Find(presetName) is { } preset
            ? (preset.PanelR, preset.PanelG, preset.PanelB)
            : (DefaultPanelR, DefaultPanelG, DefaultPanelB);
    }

    public static ThemePreset? Find(string? name)
    {
        return All.FirstOrDefault(preset => string.Equals(preset.Name, name, StringComparison.Ordinal));
    }

    public static double ShadowStrengthFor(string? name)
    {
        return Find(name)?.ShadowStrength ?? 1.0;
    }
}
