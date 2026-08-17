using System.IO;
using System.Text;

namespace TransparentCalendar.Services;

/// <summary>
/// 极简落盘日志。存在的理由：应用里十几处 catch 都是静默吞掉的，
/// 一旦用户报"设置打不开 / 笔记没保存"，没有日志就完全无从下手。
///
/// 未调用 <see cref="Initialize"/> 前所有写入都是空操作，因此单元测试与
/// 启动早期调用都不会抛异常或产生副作用。
/// </summary>
public static class Log
{
    private const int KeepDays = 7;

    private static readonly object Sync = new();
    private static string? _directory;

    public static void Initialize(string logDirectory)
    {
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(logDirectory);
                _directory = logDirectory;
                PruneOldLogs(logDirectory);
            }
            catch
            {
                // 日志目录建不出来就退化为不记日志，绝不能影响主流程。
                _directory = null;
            }
        }
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception? exception = null) => Write("WARN", message, exception);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        lock (Sync)
        {
            if (_directory is null)
            {
                return;
            }

            try
            {
                var builder = new StringBuilder()
                    .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                    .Append(' ')
                    .Append(level)
                    .Append(' ')
                    .Append(message);

                if (exception is not null)
                {
                    builder.AppendLine().Append(exception);
                }

                builder.AppendLine();

                var path = Path.Combine(_directory, $"{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(path, builder.ToString(), Encoding.UTF8);
            }
            catch
            {
                // 写日志本身失败时不能再抛，否则会淹没真正的错误。
            }
        }
    }

    private static void PruneOldLogs(string directory)
    {
        var cutoff = DateTime.Today.AddDays(-KeepDays);
        foreach (var file in Directory.EnumerateFiles(directory, "*.log"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (!DateTime.TryParseExact(
                    name,
                    "yyyyMMdd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var date)
                || date >= cutoff)
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch
            {
                // 单个旧日志删不掉不影响主流程。
            }
        }
    }
}
