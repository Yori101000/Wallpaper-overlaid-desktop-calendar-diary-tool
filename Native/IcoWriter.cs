using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace TransparentCalendar.Native;

/// <summary>
/// 把多个尺寸的位图打包成一个 .ico 文件。
/// .NET 没有内置的多尺寸 ICO 编码器，所以这里直接按 ICO 容器格式写 —— 格式很简单：
/// 6 字节文件头 + 每张图 16 字节目录项 + 各张 PNG 数据。
/// </summary>
public static class IcoWriter
{
    public static void Write(string path, IReadOnlyList<int> sizes, int day)
    {
        var images = new List<byte[]>();
        foreach (var size in sizes)
        {
            using var bitmap = AppIcon.CreateBitmap(size, day);
            using var buffer = new MemoryStream();
            bitmap.Save(buffer, ImageFormat.Png);
            images.Add(buffer.ToArray());
        }

        using var file = File.Create(path);
        using var writer = new BinaryWriter(file);

        // ICONDIR
        writer.Write((short)0);              // reserved
        writer.Write((short)1);              // type: 1 = icon
        writer.Write((short)sizes.Count);

        // 目录项之后就是图像数据
        var offset = 6 + 16 * sizes.Count;
        for (var i = 0; i < sizes.Count; i++)
        {
            // 256 在 ICO 里用 0 表示
            writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            writer.Write((byte)0);           // 调色板数量（PNG 不用）
            writer.Write((byte)0);           // reserved
            writer.Write((short)1);          // color planes
            writer.Write((short)32);         // bits per pixel
            writer.Write(images[i].Length);
            writer.Write(offset);
            offset += images[i].Length;
        }

        foreach (var image in images)
        {
            writer.Write(image);
        }
    }
}
