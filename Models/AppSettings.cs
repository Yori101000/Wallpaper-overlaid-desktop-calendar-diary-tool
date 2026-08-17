using System.Text.Json.Serialization;

namespace TransparentCalendar.Models;

/// <summary>窗口层级取值。</summary>
public static class WindowLayers
{
    /// <summary>置底：永远在其他窗口之下，但仍显示在桌面壁纸之上。</summary>
    public const string Bottom = "Bottom";

    public const string Normal = "Normal";

    public const string Top = "Top";

    /// <summary>
    /// 嵌入桌面：把窗口挂到 WorkerW 之下，落在**桌面图标之下、壁纸之上**。
    /// 与 <see cref="Bottom"/> 的区别就是图标 —— Bottom 仍在图标之上。
    /// Wallpaper Engine 占的是同一层，两者同时开很可能被它盖住，见 `Native/DesktopLayerProbe`。
    /// </summary>
    public const string Desktop = "Desktop";
}

public sealed class AppSettings
{
    public double Left { get; set; } = 80;
    public double Top { get; set; } = 80;
    public double Width { get; set; } = 760;
    public double Height { get; set; } = 520;
    public double TextOpacity { get; set; } = 0.9;
    public double FontSize { get; set; } = 28;
    public string ThemePreset { get; set; } = "清晰白";
    public string SidebarPosition { get; set; } = "Left";
    public string TextColor { get; set; } = "#FFFFFFFF";
    public bool IsLocked { get; set; }
    public bool StartWithMonday { get; set; } = true;
    public double BackgroundOpacity { get; set; } = 0.35;
    public bool StartOnBoot { get; set; }
    // 默认必须留空而**不是** WindowLayers.Normal：否则 Normalize() 会把"字段缺失"
    // 误判为"已是合法值"，从而永远不去读旧的 KeepOnTop —— 老用户的置顶设置会被静默丢弃。
    public string WindowLayer { get; set; } = string.Empty;
    public bool CloseToTray { get; set; } = true;
    public bool StartInTray { get; set; }

    /// <summary>显示农历、节气与传统节日。完全离线。</summary>
    public bool ShowLunar { get; set; } = true;

    /// <summary>
    /// 显示法定节假日与调休角标。这是应用**唯一**的对外网络请求：
    /// 调休无算法可推，只能拉取公开数据源，首次获取后会缓存到本地离线可用。
    /// </summary>
    public bool ShowStatutoryHolidays { get; set; } = true;

    // 旧版本字段，仅用于一次性迁移到 WindowLayer；迁移后不再写回文件。
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? KeepOnTop { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AttachToDesktopLayer { get; set; }

    /// <summary>把历史设置迁移到当前字段，并夹住非法取值。载入后立即调用。</summary>
    public AppSettings Normalize()
    {
        if (!IsKnownLayer(WindowLayer))
        {
            WindowLayer = KeepOnTop == true ? WindowLayers.Top : WindowLayers.Normal;
        }

        KeepOnTop = null;
        AttachToDesktopLayer = null;
        return this;
    }

    public AppSettings Clone()
    {
        return (AppSettings)MemberwiseClone();
    }

    private static bool IsKnownLayer(string? layer)
    {
        return layer is WindowLayers.Bottom or WindowLayers.Normal or WindowLayers.Top or WindowLayers.Desktop;
    }
}
