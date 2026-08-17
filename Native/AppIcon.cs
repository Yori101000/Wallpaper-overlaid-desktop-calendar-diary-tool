using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace TransparentCalendar.Native;

/// <summary>
/// 程序化绘制应用图标 —— 一个日历方块加当日数字。
/// 这样托盘图标能随日期变化，也免去了维护一份美术资源。
/// </summary>
public static class AppIcon
{
    private static readonly Color HeaderColor = Color.FromArgb(239, 71, 111);
    private static readonly Color BodyColor = Color.FromArgb(250, 250, 252);
    private static readonly Color TextColor = Color.FromArgb(32, 32, 40);

    /// <summary>绘制指定日期的日历图标位图。</summary>
    public static Bitmap CreateBitmap(int size, int day)
    {
        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.Clear(Color.Transparent);

        var padding = Math.Max(1, size / 16);
        var rect = new Rectangle(padding, padding, size - padding * 2, size - padding * 2);
        var radius = Math.Max(2, size / 8);

        using (var body = new SolidBrush(BodyColor))
        using (var path = RoundedRect(rect, radius))
        {
            graphics.FillPath(body, path);
        }

        // 顶部红色标题条（用整块圆角矩形再裁掉下半部分，保证上圆下方）
        var headerHeight = Math.Max(3, rect.Height / 4);
        var headerRect = new Rectangle(rect.X, rect.Y, rect.Width, headerHeight);
        using (var header = new SolidBrush(HeaderColor))
        using (var path = RoundedRect(new Rectangle(rect.X, rect.Y, rect.Width, radius * 2), radius))
        {
            graphics.FillPath(header, path);
            graphics.FillRectangle(
                header,
                rect.X,
                rect.Y + radius,
                rect.Width,
                Math.Max(1, headerHeight - radius));
        }

        // 装订环
        if (size >= 32)
        {
            var ringWidth = Math.Max(2, size / 16);
            var ringHeight = Math.Max(3, size / 10);
            var ringY = rect.Y - ringHeight / 3;
            using var ring = new SolidBrush(Color.FromArgb(210, 240, 240, 245));
            graphics.FillRectangle(ring, rect.X + rect.Width / 4 - ringWidth / 2, ringY, ringWidth, ringHeight);
            graphics.FillRectangle(ring, rect.X + rect.Width * 3 / 4 - ringWidth / 2, ringY, ringWidth, ringHeight);
        }

        DrawDayNumber(graphics, rect, headerHeight, size, day);

        return bitmap;
    }

    private static void DrawDayNumber(Graphics graphics, Rectangle rect, int headerHeight, int size, int day)
    {
        var text = day.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var bodyRect = new Rectangle(
            rect.X,
            rect.Y + headerHeight,
            rect.Width,
            rect.Height - headerHeight);

        // 16px 这种尺寸下两位数字会糊成一团，改画一条横杠示意
        if (size < 20)
        {
            using var bar = new SolidBrush(TextColor);
            var barHeight = Math.Max(1, bodyRect.Height / 4);
            graphics.FillRectangle(
                bar,
                bodyRect.X + bodyRect.Width / 4,
                bodyRect.Y + (bodyRect.Height - barHeight) / 2,
                bodyRect.Width / 2,
                barHeight);
            return;
        }

        var fontSize = bodyRect.Height * 0.72f;
        using var font = new Font("Segoe UI", fontSize, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(TextColor);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        graphics.DrawString(text, font, brush, bodyRect, format);
    }

    private static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;

        if (diameter >= rect.Width || diameter >= rect.Height)
        {
            path.AddRectangle(rect);
            return path;
        }

        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// 生成托盘图标。调用方负责 Dispose —— NotifyIcon 不会替你释放 GDI 句柄，
    /// 每天刷新一次的话泄漏会累积。
    /// </summary>
    public static Icon CreateTrayIcon(int day)
    {
        using var bitmap = CreateBitmap(32, day);
        var handle = bitmap.GetHicon();
        try
        {
            // Icon.FromHandle 不拥有句柄，必须克隆出一个自持有的实例再销毁原句柄。
            using var shared = Icon.FromHandle(handle);
            return (Icon)shared.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
