using TransparentCalendar.Models;
using Xunit;

namespace TransparentCalendar.Tests;

/// <summary>
/// 农历换算的基准日期。这些日期与国务院放假安排（holiday-cn / timor.tech）交叉核对过：
/// 2026 年端午假期自 06-19 起、中秋假期自 09-25 起、春节假期含 02-16（除夕）与 02-17（初一）。
/// </summary>
public class LunarCalendarTests
{
    [Theory]
    [InlineData(2026, 2, 17, "春节")]
    [InlineData(2026, 2, 16, "除夕")]
    [InlineData(2026, 3, 3, "元宵")]
    [InlineData(2026, 6, 19, "端午")]
    [InlineData(2026, 9, 25, "中秋")]
    public void 传统节日落在正确的公历日期(int year, int month, int day, string festival)
    {
        var info = LunarCalendar.GetInfo(new DateTime(year, month, day));
        Assert.Equal(festival, info.Festival);
    }

    [Fact]
    public void 春节当天农历是正月初一()
    {
        var info = LunarCalendar.GetInfo(new DateTime(2026, 2, 17));

        Assert.Equal("正", info.MonthName);
        Assert.Equal("正月", info.DayName);
    }

    [Fact]
    public void 初一显示月名而非初一()
    {
        // 每月第一天显示"正月"这样的月名，方便在密集的日历格里定位月份边界
        var info = LunarCalendar.GetInfo(new DateTime(2026, 2, 17));
        Assert.Equal("正月", info.DayName);
    }

    [Theory]
    [InlineData(2026, 2, 18, "初二")]
    [InlineData(2026, 2, 26, "初十")]
    [InlineData(2026, 3, 3, "十五")]
    [InlineData(2026, 6, 19, "初五")]  // 端午 = 五月初五
    public void 农历日名格式正确(int year, int month, int day, string expected)
    {
        var info = LunarCalendar.GetInfo(new DateTime(year, month, day));
        Assert.Equal(expected, info.DayName);
    }

    [Fact]
    public void 除夕靠次日是初一判定_不假设腊月固定三十天()
    {
        var chuxi = LunarCalendar.GetInfo(new DateTime(2026, 2, 16));
        var chunjie = LunarCalendar.GetInfo(new DateTime(2026, 2, 17));

        Assert.Equal("除夕", chuxi.Festival);
        Assert.Equal("春节", chunjie.Festival);
    }

    [Fact]
    public void 普通日子没有节日()
    {
        var info = LunarCalendar.GetInfo(new DateTime(2026, 8, 14));

        Assert.Null(info.Festival);
        Assert.False(string.IsNullOrEmpty(info.DayName));
    }

    [Fact]
    public void Label_优先显示节日()
    {
        var info = new LunarInfo("正", "初一", "春节", "立春");
        Assert.Equal("春节", info.Label);
        Assert.True(info.IsHighlighted);
    }

    [Fact]
    public void Label_没有节日时显示节气()
    {
        var info = new LunarInfo("七", "廿三", null, "立秋");
        Assert.Equal("立秋", info.Label);
        Assert.True(info.IsHighlighted);
    }

    [Fact]
    public void Label_都没有时显示农历日且不高亮()
    {
        var info = new LunarInfo("七", "廿三", null, null);
        Assert.Equal("廿三", info.Label);
        Assert.False(info.IsHighlighted);
    }

    [Fact]
    public void 超出内置日历支持范围时安全退化而不抛异常()
    {
        var tooEarly = LunarCalendar.GetInfo(new DateTime(1800, 1, 1));
        var tooLate = LunarCalendar.GetInfo(new DateTime(2200, 1, 1));

        Assert.Null(tooEarly.Festival);
        Assert.Null(tooLate.Festival);
    }

    [Fact]
    public void 整年换算不抛异常()
    {
        for (var date = new DateTime(2026, 1, 1); date < new DateTime(2027, 1, 1); date = date.AddDays(1))
        {
            var info = LunarCalendar.GetInfo(date);
            Assert.False(string.IsNullOrEmpty(info.Label));
        }
    }

    [Theory]
    // 2026 全年 24 节气 —— 逐个钉死，任何常数改动导致的整体偏移都会被立刻抓到
    [InlineData(2026, 1, 5, "小寒")]
    [InlineData(2026, 1, 20, "大寒")]
    [InlineData(2026, 2, 4, "立春")]
    [InlineData(2026, 2, 18, "雨水")]
    [InlineData(2026, 3, 5, "惊蛰")]
    [InlineData(2026, 3, 20, "春分")]
    [InlineData(2026, 4, 5, "清明")]
    [InlineData(2026, 4, 20, "谷雨")]
    [InlineData(2026, 5, 5, "立夏")]
    [InlineData(2026, 5, 21, "小满")]
    [InlineData(2026, 6, 5, "芒种")]
    [InlineData(2026, 6, 21, "夏至")]
    [InlineData(2026, 7, 7, "小暑")]
    [InlineData(2026, 7, 23, "大暑")]
    [InlineData(2026, 8, 7, "立秋")]
    [InlineData(2026, 8, 23, "处暑")]
    [InlineData(2026, 9, 7, "白露")]
    [InlineData(2026, 9, 23, "秋分")]
    [InlineData(2026, 10, 8, "寒露")]
    [InlineData(2026, 10, 23, "霜降")]
    [InlineData(2026, 11, 7, "立冬")]
    [InlineData(2026, 11, 22, "小雪")]
    [InlineData(2026, 12, 7, "大雪")]
    [InlineData(2026, 12, 22, "冬至")]
    public void 节气落在正确日期(int year, int month, int day, string term)
    {
        Assert.Equal(term, LunarCalendar.GetSolarTerm(new DateTime(year, month, day)));
    }

    [Theory]
    // 已知例外年份的修正必须生效
    [InlineData(2019, 1, 5, "小寒")]
    [InlineData(2021, 12, 21, "冬至")]
    public void 已知例外年份的修正生效(int year, int month, int day, string term)
    {
        Assert.Equal(term, LunarCalendar.GetSolarTerm(new DateTime(year, month, day)));
    }

    [Fact]
    public void 每年恰好有二十四个节气()
    {
        var count = 0;
        for (var date = new DateTime(2026, 1, 1); date < new DateTime(2027, 1, 1); date = date.AddDays(1))
        {
            if (LunarCalendar.GetSolarTerm(date) is not null)
            {
                count++;
            }
        }

        Assert.Equal(24, count);
    }
}
