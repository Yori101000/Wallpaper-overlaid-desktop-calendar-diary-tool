namespace TransparentCalendar.Services;

/// <summary>
/// 网址校验。笔记的 URL 可能来自浏览器扩展、bookmarklet 或用户手输，
/// 而打开它时走的是 ShellExecute —— 因此必须限制为 http/https。
/// </summary>
public static class WebUrl
{
    /// <summary>校验协议并去除首尾空白，保留查询串等原样。</summary>
    public static bool TryValidate(string? url, out string validated)
    {
        validated = string.Empty;
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        validated = uri.AbsoluteUri;
        return true;
    }

    /// <summary>
    /// 归一化为 scheme://host/path（去掉查询串与尾部斜杠），用于把同一页面的多次划线归到一组。
    /// </summary>
    public static bool TryNormalize(string? url, out string normalized)
    {
        normalized = string.Empty;
        if (!TryValidate(url, out var validated))
        {
            return false;
        }

        var uri = new Uri(validated);
        normalized = uri.Scheme + "://" + uri.Host + uri.AbsolutePath.TrimEnd('/');
        return true;
    }

    public static string ExtractDomain(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;
    }
}
