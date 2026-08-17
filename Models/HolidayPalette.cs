using System.Globalization;

namespace TransparentCalendar.Models;

/// <summary>
/// 「休 / 班」两支颜色的取色规则。
///
/// 数字颜色这条通道同时被两件事使用：用户在设置里选的文字色（普通日期）与法定属性
/// （放假 / 调休）。一旦文字色和其中一支撞了色相，那一支就等于失效 —— 主题预设
/// 「柔和青」的 <c>#7BDFF2</c> 距基准休色只有 26°，「暖金」的 <c>#FFD166</c>
/// 距基准班色只有 15°，换过去以后放假日和普通日看起来一模一样。
///
/// 所以这里不做"预设 → 配色"的对照表，而是按文字色现算：每一支都在自己的候选序列里
/// 挑第一个离文字色足够远的颜色。自定义文字色因此一样受保护。
///
/// 纯逻辑、无 UI 依赖（颜色以十六进制串进出，不碰 <c>System.Windows.Media</c>），
/// 方便 <c>Tests/</c> 覆盖。
/// </summary>
public static class HolidayPalette
{
    /// <summary>基准休色：青绿，色相 ≈163°。</summary>
    public const string BaseOff = "#7DE8D0";

    /// <summary>基准班色：橙，色相 ≈30°。</summary>
    public const string BaseWork = "#FFC078";

    /// <summary>备用休色：黄绿，色相 ≈92°。文字色偏青时启用。</summary>
    public const string AltOff = "#9BE870";

    /// <summary>
    /// 备用班色：玫红，色相 ≈337°。文字色偏金/橙时启用。
    /// 比"重要"圆点的 <c>#FF6B8A</c> 更粉更亮，错开一档 —— 两者一个是数字一个是圆点，
    /// 本来就不同族，但没必要撞得那么近。
    /// </summary>
    public const string AltWork = "#FF7BA8";

    /// <summary>
    /// 第三支班色：紫，色相 ≈272°。红系文字色需要它 —— 纯红（0°）距基准橙只有 30°、
    /// 距备用玫红只有 23°，两支都不够远。
    /// </summary>
    public const string FarWork = "#C58AFF";

    /// <summary>低于此饱和度（白、浅灰、高对比）没有色相可撞，直接用基准两支。</summary>
    private const double MinSaturation = 0.15;

    /// <summary>与文字色至少要拉开这么多色相，才算"能分得开"。</summary>
    private const double SafeHueDistance = 45;

    // 候选按**偏好顺序**排列：能用基准就用基准。
    // 休只有两支：两支之间唯一的缺口在 120° 附近（纯绿文字），那时基准仍有 43°，够用。
    private static readonly string[] OffCandidates = [BaseOff, AltOff];
    private static readonly string[] WorkCandidates = [BaseWork, AltWork, FarWork];

    /// <summary>按当前文字色决定「休 / 班」两支该用哪个候选。</summary>
    public static (string Off, string Work) Resolve(string? textColor)
    {
        if (!TryParse(textColor, out var r, out var g, out var b))
        {
            return (BaseOff, BaseWork);
        }

        var (hue, saturation) = HueSaturation(r, g, b);
        if (saturation < MinSaturation)
        {
            return (BaseOff, BaseWork);
        }

        return (Pick(hue, OffCandidates), Pick(hue, WorkCandidates));
    }

    /// <summary>
    /// 挑第一个离文字色不小于 <see cref="SafeHueDistance"/> 的候选；一个都不达标时退而取最远的。
    ///
    /// 两条都是必要的：
    /// <list type="bullet">
    /// <item>**顺序优先**避免无谓换色 —— 「柔和青」只撞休，班就该保持基准橙，
    /// 而不是因为"还有更远的紫"把两支都换掉。</item>
    /// <item>**兜底取最远**避免阈值坑 —— 纯绿文字（120°）距基准休色 43°、距备用休色只有 28°，
    /// 若无脑"不达标就换下一个"，反而撞得更狠。</item>
    /// </list>
    /// </summary>
    private static string Pick(double textHue, string[] candidates)
    {
        var best = candidates[0];
        var bestDistance = -1.0;

        foreach (var candidate in candidates)
        {
            var distance = HueDistance(textHue, HueOf(candidate));
            if (distance >= SafeHueDistance)
            {
                return candidate;
            }

            if (distance > bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>两个色相的环形距离（0~180）。</summary>
    public static double HueDistance(double left, double right)
    {
        var delta = Math.Abs(left - right) % 360;
        return delta > 180 ? 360 - delta : delta;
    }

    /// <summary>十六进制颜色串的色相；无法解析时返回 0。</summary>
    public static double HueOf(string? color)
    {
        return TryParse(color, out var r, out var g, out var b) ? HueSaturation(r, g, b).Hue : 0;
    }

    /// <summary>十六进制颜色串的饱和度（HSV 的 S，0~1）；无法解析时返回 0。</summary>
    public static double SaturationOf(string? color)
    {
        return TryParse(color, out var r, out var g, out var b) ? HueSaturation(r, g, b).Saturation : 0;
    }

    private static (double Hue, double Saturation) HueSaturation(byte r, byte g, byte b)
    {
        var red = r / 255.0;
        var green = g / 255.0;
        var blue = b / 255.0;
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var span = max - min;

        var saturation = max <= 0 ? 0 : span / max;
        if (span <= 0)
        {
            return (0, saturation);
        }

        double hue;
        if (max == red)
        {
            hue = 60 * (((green - blue) / span) % 6);
        }
        else if (max == green)
        {
            hue = 60 * (((blue - red) / span) + 2);
        }
        else
        {
            hue = 60 * (((red - green) / span) + 4);
        }

        return (hue < 0 ? hue + 360 : hue, saturation);
    }

    /// <summary>接受 <c>#RRGGBB</c> 与 <c>#AARRGGBB</c>（井号可省）。alpha 与色相无关，直接丢掉。</summary>
    private static bool TryParse(string? color, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(color))
        {
            return false;
        }

        var text = color.Trim().TrimStart('#');
        if (text.Length == 8)
        {
            text = text[2..];
        }

        if (text.Length != 6
            || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        r = (byte)((value >> 16) & 0xFF);
        g = (byte)((value >> 8) & 0xFF);
        b = (byte)(value & 0xFF);
        return true;
    }
}
