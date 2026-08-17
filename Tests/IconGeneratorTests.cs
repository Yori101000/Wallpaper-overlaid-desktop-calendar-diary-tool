using System.IO;
using TransparentCalendar.Native;
using Xunit;

namespace TransparentCalendar.Tests;

/// <summary>
/// 图标是程序化绘制的，这里既验证它不抛异常，也顺带把 Assets\app.ico 生成出来
/// （设为 csproj 的 ApplicationIcon）。设 TC_WRITE_ICON=1 时才写文件。
/// </summary>
public class IconGeneratorTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(48)]
    [InlineData(256)]
    public void 各尺寸都能绘制(int size)
    {
        using var bitmap = AppIcon.CreateBitmap(size, 14);

        Assert.Equal(size, bitmap.Width);
        Assert.Equal(size, bitmap.Height);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(31)]
    public void 一位与两位日期都能绘制(int day)
    {
        using var bitmap = AppIcon.CreateBitmap(32, day);
        Assert.NotNull(bitmap);
    }

    [Fact]
    public void 托盘图标可生成且可释放()
    {
        var icon = AppIcon.CreateTrayIcon(14);

        Assert.NotNull(icon);
        icon.Dispose();
    }

    [Fact]
    public void 生成应用图标文件()
    {
        if (Environment.GetEnvironmentVariable("TC_WRITE_ICON") != "1")
        {
            return;
        }

        var repoRoot = FindRepoRoot();
        var assets = Path.Combine(repoRoot, "Assets");
        Directory.CreateDirectory(assets);

        IcoWriter.Write(Path.Combine(assets, "app.ico"), [16, 32, 48, 64, 128, 256], day: 14);

        Assert.True(File.Exists(Path.Combine(assets, "app.ico")));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "透明日历.csproj")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("找不到仓库根目录。");
    }
}
