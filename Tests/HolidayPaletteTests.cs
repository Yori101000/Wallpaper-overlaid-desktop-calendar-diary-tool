using TransparentCalendar.Models;
using Xunit;

namespace TransparentCalendar.Tests;

/// <summary>
/// 假日两支颜色的避让规则。这些用例钉住的是"换到任何主题预设，休/班都还能和普通日期
/// 区分开"—— 曾经「柔和青」的文字色与休色只差 26°、「暖金」与班色只差 15°，
/// 那两个预设下颜色这条通道等于失效。
/// </summary>
public class HolidayPaletteTests
{
    /// <summary>与文字色的最小可接受色相距离。</summary>
    private const double MinTextDistance = 40;

    [Fact]
    public void 清晰白_用基准两支()
    {
        var (off, work) = HolidayPalette.Resolve("#FFFFFFFF");
        Assert.Equal(HolidayPalette.BaseOff, off);
        Assert.Equal(HolidayPalette.BaseWork, work);
    }

    [Fact]
    public void 高对比_也是白色_用基准两支()
    {
        var (off, work) = HolidayPalette.Resolve("#FFFFFFFF");
        Assert.Equal(HolidayPalette.BaseOff, off);
        Assert.Equal(HolidayPalette.BaseWork, work);
    }

    [Fact]
    public void 柔和青_把休换成备用色_班不动()
    {
        var (off, work) = HolidayPalette.Resolve("#FF7BDFF2");
        Assert.Equal(HolidayPalette.AltOff, off);
        Assert.Equal(HolidayPalette.BaseWork, work);
    }

    [Fact]
    public void 暖金_把班换成备用色_休不动()
    {
        var (off, work) = HolidayPalette.Resolve("#FFFFD166");
        Assert.Equal(HolidayPalette.BaseOff, off);
        Assert.Equal(HolidayPalette.AltWork, work);
    }

    /// <summary>
    /// 纯绿距基准休色 43°、距备用休色只有 28° —— 两支都不达安全线，
    /// 这时必须退回"取最远的那个"，而不是无脑换到下一个候选。
    /// </summary>
    [Fact]
    public void 纯绿文字_休退回基准色_而不是换到更近的备用色()
    {
        var (off, _) = HolidayPalette.Resolve("#00FF00");
        Assert.Equal(HolidayPalette.BaseOff, off);
        Assert.NotEqual(HolidayPalette.AltOff, off);
    }

    /// <summary>
    /// 纯红距基准橙 30°、距备用玫红 23°，两支都不够远 —— 这是加第三支紫色的原因。
    /// </summary>
    [Fact]
    public void 纯红文字_班换到第三支紫色()
    {
        var (_, work) = HolidayPalette.Resolve("#FF0000");
        Assert.Equal(HolidayPalette.FarWork, work);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("垃圾")]
    [InlineData("#FFF")]
    [InlineData("#GGGGGG")]
    public void 无法解析的颜色_回退到基准两支(string? color)
    {
        var (off, work) = HolidayPalette.Resolve(color);
        Assert.Equal(HolidayPalette.BaseOff, off);
        Assert.Equal(HolidayPalette.BaseWork, work);
    }

    [Theory]
    [InlineData("#FFFFFF")]
    [InlineData("FFFFFF")]
    [InlineData("#FF7BDFF2")]
    [InlineData("7BDFF2")]
    public void 井号与_alpha_都可省略(string color)
    {
        var (off, work) = HolidayPalette.Resolve(color);
        Assert.False(string.IsNullOrEmpty(off));
        Assert.False(string.IsNullOrEmpty(work));
    }

    [Theory]
    [InlineData("#EEEEEE")]  // 浅灰：没有色相可撞
    [InlineData("#888888")]
    public void 低饱和度的文字色_不触发避让(string color)
    {
        var (off, work) = HolidayPalette.Resolve(color);
        Assert.Equal(HolidayPalette.BaseOff, off);
        Assert.Equal(HolidayPalette.BaseWork, work);
    }

    [Fact]
    public void 今天色_白色文字用基准天蓝()
    {
        Assert.Equal(HolidayPalette.BaseToday, HolidayPalette.ResolveToday("#FFFFFFFF"));
    }

    /// <summary>
    /// 「今天」的天蓝（211°）与日记圆点的青（189°）只差 22°，但那是**圆点**、这是**数字**，
    /// 族别不同。真正要避的是同为数字颜色的休与班，以及用户自己的文字色。
    /// </summary>
    [Theory]
    [InlineData("#FFFFFF")]
    [InlineData("#7BDFF2")]
    [InlineData("#FFD166")]
    [InlineData("#FF0000")]
    [InlineData("#00FF00")]
    [InlineData("#6FB3FF")]
    public void 今天色_与休班和文字色都拉得开(string textColor)
    {
        var today = HolidayPalette.ResolveToday(textColor);
        var (off, work) = HolidayPalette.Resolve(textColor);
        var todayHue = HolidayPalette.HueOf(today);

        Assert.True(HolidayPalette.HueDistance(todayHue, HolidayPalette.HueOf(off)) >= 45,
            $"今天色与休色撞色：{today} / {off}");
        Assert.True(HolidayPalette.HueDistance(todayHue, HolidayPalette.HueOf(work)) >= 45,
            $"今天色与班色撞色：{today} / {work}");

        if (HolidayPalette.SaturationOf(textColor) >= 0.15)
        {
            Assert.True(
                HolidayPalette.HueDistance(todayHue, HolidayPalette.HueOf(textColor)) >= 40,
                $"今天色与文字色撞色：{today} vs {textColor}");
        }
    }

    /// <summary>
    /// 不变量：无论文字色是什么，休与班之间必须拉开（否则两种法定属性互相混），
    /// 且各自与文字色拉开（否则和普通日期混）。饱和度过低的文字色不参与后一条。
    /// </summary>
    [Theory]
    [InlineData("#FFFFFF")]
    [InlineData("#7BDFF2")]
    [InlineData("#FFD166")]
    [InlineData("#00FF00")]
    [InlineData("#FF0000")]
    [InlineData("#0000FF")]
    [InlineData("#FF00FF")]
    [InlineData("#00FFFF")]
    [InlineData("#7DE8D0")]
    [InlineData("#FFC078")]
    public void 休与班之间_以及与文字色之间_都保持可辨距离(string textColor)
    {
        var (off, work) = HolidayPalette.Resolve(textColor);

        var offHue = HolidayPalette.HueOf(off);
        var workHue = HolidayPalette.HueOf(work);
        Assert.True(
            HolidayPalette.HueDistance(offHue, workHue) >= 60,
            $"休与班撞色：{off} / {work}");

        if (HolidayPalette.SaturationOf(textColor) < 0.15)
        {
            return;
        }

        var textHue = HolidayPalette.HueOf(textColor);
        Assert.True(
            HolidayPalette.HueDistance(textHue, offHue) >= MinTextDistance,
            $"休色与文字色撞色：{off} vs {textColor}");
        Assert.True(
            HolidayPalette.HueDistance(textHue, workHue) >= MinTextDistance,
            $"班色与文字色撞色：{work} vs {textColor}");
    }
}
