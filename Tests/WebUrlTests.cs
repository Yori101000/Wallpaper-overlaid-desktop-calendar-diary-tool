using TransparentCalendar.Services;
using Xunit;

namespace TransparentCalendar.Tests;

/// <summary>
/// 网址校验直接决定两件事：ShellExecute 会不会被喂进任意协议，
/// 以及同一页面的多次划线能不能归到一组。
/// </summary>
public class WebUrlTests
{
    [Theory]
    [InlineData("https://example.com/a")]
    [InlineData("http://example.com")]
    [InlineData("HTTPS://Example.com/Path")]
    public void TryValidate_接受_http_与_https(string url)
    {
        Assert.True(WebUrl.TryValidate(url, out var validated));
        Assert.NotEmpty(validated);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/system32")]
    [InlineData("ftp://example.com/x")]
    [InlineData("ms-settings:privacy")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("不是网址")]
    public void TryValidate_拒绝非_http_协议与空值(string? url)
    {
        Assert.False(WebUrl.TryValidate(url, out var validated));
        Assert.Equal(string.Empty, validated);
    }

    [Fact]
    public void TryValidate_去除首尾空白()
    {
        Assert.True(WebUrl.TryValidate("  https://example.com/a  ", out var validated));
        Assert.StartsWith("https://example.com/a", validated, StringComparison.Ordinal);
    }

    [Theory]
    // 查询串与片段会被剥离，尾部斜杠会被去掉 —— 同一页面的多次划线才能归到同一组
    [InlineData("https://example.com/a/?q=1", "https://example.com/a")]
    [InlineData("https://example.com/a", "https://example.com/a")]
    [InlineData("https://example.com/a/", "https://example.com/a")]
    [InlineData("https://example.com/a#section", "https://example.com/a")]
    [InlineData("https://example.com", "https://example.com")]
    public void TryNormalize_把同一页面的不同写法归一(string input, string expected)
    {
        Assert.True(WebUrl.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void TryNormalize_保留大小写敏感的路径()
    {
        Assert.True(WebUrl.TryNormalize("https://example.com/Path/Sub", out var normalized));
        Assert.Equal("https://example.com/Path/Sub", normalized);
    }

    [Theory]
    [InlineData("javascript:void(0)")]
    [InlineData("file:///C:/")]
    public void TryNormalize_同样拒绝非_http_协议(string url)
    {
        Assert.False(WebUrl.TryNormalize(url, out _));
    }

    [Fact]
    public void ExtractDomain_取主机名()
    {
        Assert.Equal("example.com", WebUrl.ExtractDomain("https://example.com/a/b"));
    }

    [Fact]
    public void ExtractDomain_对非法输入原样返回()
    {
        Assert.Equal("垃圾", WebUrl.ExtractDomain("垃圾"));
    }
}
