using TransparentCalendar.Models;
using Xunit;

namespace TransparentCalendar.Tests;

/// <summary>
/// 设置迁移一旦回归，用户的窗口层级会被悄悄重置。这里锁死迁移规则。
/// </summary>
public class AppSettingsTests
{
    [Fact]
    public void Normalize_把旧的_KeepOnTop_true_迁移为置顶()
    {
        var settings = new AppSettings { WindowLayer = null!, KeepOnTop = true };

        settings.Normalize();

        Assert.Equal(WindowLayers.Top, settings.WindowLayer);
    }

    [Fact]
    public void Normalize_把旧的_KeepOnTop_false_迁移为普通()
    {
        var settings = new AppSettings { WindowLayer = null!, KeepOnTop = false };

        settings.Normalize();

        Assert.Equal(WindowLayers.Normal, settings.WindowLayer);
    }

    [Fact]
    public void Normalize_清空旧字段以便它们不再写回文件()
    {
        var settings = new AppSettings { WindowLayer = null!, KeepOnTop = true, AttachToDesktopLayer = true };

        settings.Normalize();

        Assert.Null(settings.KeepOnTop);
        Assert.Null(settings.AttachToDesktopLayer);
    }

    [Theory]
    [InlineData(WindowLayers.Bottom)]
    [InlineData(WindowLayers.Normal)]
    [InlineData(WindowLayers.Top)]
    [InlineData(WindowLayers.Desktop)]
    public void Normalize_保留已有的合法层级(string layer)
    {
        var settings = new AppSettings { WindowLayer = layer, KeepOnTop = true };

        settings.Normalize();

        Assert.Equal(layer, settings.WindowLayer);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Sideways")]
    public void Normalize_把非法层级夹回默认值(string layer)
    {
        var settings = new AppSettings { WindowLayer = layer };

        settings.Normalize();

        Assert.Equal(WindowLayers.Normal, settings.WindowLayer);
    }

    [Fact]
    public void Clone_产出独立副本()
    {
        var original = new AppSettings { FontSize = 30, TextColor = "#FF00FF00", WindowLayer = WindowLayers.Top };

        var copy = original.Clone();
        copy.FontSize = 12;
        copy.TextColor = "#FFFFFFFF";

        Assert.Equal(30, original.FontSize);
        Assert.Equal("#FF00FF00", original.TextColor);
        Assert.Equal(WindowLayers.Top, copy.WindowLayer);
    }
}
