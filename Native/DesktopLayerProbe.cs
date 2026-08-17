using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using TransparentCalendar.Services;

namespace TransparentCalendar.Native;

/// <summary>
/// 「把日历沉到桌面图标之下」的可行性探针。
///
/// 桌面图标是 <c>Progman</c> / <c>WorkerW</c> 下的 <c>SHELLDLL_DefView</c> → <c>SysListView32</c>。
/// 想落到图标之下、壁纸之上，唯一的路是把窗口 <c>SetParent</c> 进 WorkerW —— 而那正是
/// Wallpaper Engine 占据的层（<see cref="WindowLayerService"/> 的注释里记着上次的失败）。
///
/// 所以先探针、后决定：**不碰主窗口**，只拿一个临时窗口试，把结论写进日志。
/// 用 <c>TransparentCalendar.exe --probe-desktop-layer</c> 触发（跑完立即退出，不建主窗口）。
/// </summary>
public static class DesktopLayerProbe
{
    private const int GwlExStyle = -20;
    private const int WsExLayered = 0x00080000;
    private const uint SpawnWorkerWMessage = 0x052C;
    private const uint SendMessageTimeoutMs = 1000;
    private const uint SmtoAbortIfHung = 0x0002;
    private const uint GaParent = 1;

    /// <summary>Wallpaper Engine 的渲染进程名（32 / 64 位两种）。</summary>
    private static readonly string[] WallpaperEngineProcesses = ["wallpaper32", "wallpaper64"];

    /// <summary>单个候选宿主的试探结果。</summary>
    public sealed record Attempt(
        IntPtr Host,
        string HostClass,
        bool Attached,
        bool TransparencyKept,
        bool KeyboardFocusKept)
    {
        public bool Viable => Attached && TransparencyKept && KeyboardFocusKept;
    }

    public sealed record Result(
        bool HostFound,
        bool WallpaperEngineRunning,
        IReadOnlyList<Attempt> Attempts)
    {
        /// <summary>只要有一个候选宿主三项全过，方案就值得往下做。</summary>
        public bool Viable => Attempts.Any(attempt => attempt.Viable);
    }

    /// <summary>
    /// 跑一遍探针并把结论写进日志。必须在 UI 线程上调用（要建 WPF 窗口）。
    /// </summary>
    public static Result Run()
    {
        var weRunning = IsWallpaperEngineRunning();
        Log.Info($"[探针] Wallpaper Engine {(weRunning ? "正在运行" : "未运行")}。");

        var candidates = FindHostCandidates();
        if (candidates.Count == 0)
        {
            Log.Warn("[探针] 找不到任何 Progman / WorkerW，桌面层方案无从下手。");
            return new Result(false, weRunning, []);
        }

        // 命中即止。临时窗口关掉之后本进程的窗口创建会受影响（后续 SetParent 一律报
        // 1400 ERROR_INVALID_WINDOW_HANDLE），继续试只会往日志里灌假失败。
        var attempts = new List<Attempt>();
        foreach (var candidate in candidates)
        {
            var attempt = TryAttach(candidate);
            attempts.Add(attempt);
            if (attempt.Viable)
            {
                break;
            }
        }

        var result = new Result(true, weRunning, attempts);

        var winner = attempts.FirstOrDefault(attempt => attempt.Viable);
        if (winner is null)
        {
            Log.Info("[探针] 结论：技术上不可行 —— 没有一个候选宿主能同时保住挂载、半透明与键盘焦点。");
        }
        else
        {
            Log.Info(
                $"[探针] 结论：技术上可行，宿主 0x{winner.Host.ToInt64():X}（{winner.HostClass}）"
                + (weRunning ? " —— 但 Wallpaper Engine 正占着同一层，实际很可能被它盖住。" : "。"));
        }

        return result;
    }

    /// <summary>拿一个临时窗口试挂到指定宿主下，验挂载、半透明、键盘焦点三件事。</summary>
    private static Attempt TryAttach(IntPtr host)
    {
        var hostClass = ClassNameOf(host);

        // 临时窗口：故意做得很小并且放在屏幕外，避免在用户桌面上闪一下。
        var probe = new Window
        {
            Width = 120,
            Height = 80,
            Left = -400,
            Top = -400,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false
        };

        var textBox = new System.Windows.Controls.TextBox();
        probe.Content = textBox;

        var attached = false;
        var transparencyKept = false;
        var focusKept = false;

        try
        {
            probe.Show();
            var hwnd = new WindowInteropHelper(probe).Handle;

            // SetParent 返回的是**原来的**父窗口：顶层窗口原本没有父窗口，成功时也返回 NULL，
            // 所以不能拿返回值判成败。回读也不能用 GetParent —— 对非子窗口它返回的是 owner。
            // GetAncestor(GA_PARENT) 才是真正的父窗口。
            Marshal.SetLastSystemError(0);
            SetParent(hwnd, host);
            var error = Marshal.GetLastWin32Error();
            var ancestor = GetAncestor(hwnd, GaParent);
            attached = ancestor == host;

            Log.Info(
                $"[探针] 0x{host.ToInt64():X}（{hostClass}）：挂载{(attached ? "成功" : "失败")}"
                + $"（GetLastError={error}, GetAncestor=0x{ancestor.ToInt64():X}"
                + $"（{ClassNameOf(ancestor)}）, GetParent=0x{GetParent(hwnd).ToInt64():X}）。");

            if (attached)
            {
                // WPF 的 AllowsTransparency 靠 WS_EX_LAYERED 实现；分层**子**窗口要 Win8+ 才支持，
                // SetParent 之后这一位可能被系统清掉。
                transparencyKept = (GetWindowLong(hwnd, GwlExStyle) & WsExLayered) != 0;

                // 键盘焦点：主窗口有搜索框，输入废掉就等于方案废掉。
                textBox.Focus();
                probe.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Input);
                focusKept = textBox.IsKeyboardFocused && GetFocus() == hwnd;

                Log.Info(
                    $"[探针] 0x{host.ToInt64():X}：半透明{(transparencyKept ? "保住了" : "已失效")}，"
                    + $"键盘焦点{(focusKept ? "还在" : "拿不到")}"
                    + $"（IsKeyboardFocused={textBox.IsKeyboardFocused}, GetFocus 命中={GetFocus() == hwnd}）。");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[探针] 试探宿主 0x{host.ToInt64():X} 时抛异常。", ex);
        }
        finally
        {
            try
            {
                probe.Close();
            }
            catch (Exception ex)
            {
                Log.Warn("[探针] 关闭临时窗口失败。", ex);
            }
        }

        return new Attempt(host, hostClass, attached, transparencyKept, focusKept);
    }

    /// <summary>
    /// 候选宿主，按优先级排。两条关键规则是实测出来的：
    ///
    /// <list type="number">
    /// <item>**必须可见且有面积**。这台机器上枚举出 17 个"没有 DefView 的 WorkerW"，
    /// 绝大多数是隐藏的零尺寸僵尸窗口 —— 挂进去能挂成功，但窗口根本不显示。
    /// 所以按"可见 + 面积从大到小"筛，越接近整屏的越可能是真壁纸宿主。</item>
    /// <item>**图标挂在谁下面，就优先挂到谁下面**。若没有任何 WorkerW 带 `SHELLDLL_DefView`，
    /// 说明桌面图标（以及壁纸）由 Progman 自己承载，那 Progman 才是要进的那一层：
    /// 作为 Progman 的子窗口并压到子窗口 Z 序最底，就落在图标之下、壁纸之上。</item>
    /// </list>
    /// </summary>
    public static List<IntPtr> FindHostCandidates()
    {
        var progman = FindWindow("Progman", null);
        if (progman != IntPtr.Zero)
        {
            // 定向发给 Progman 一个窗口催生 WorkerW，**不是** HWND_BROADCAST ——
            // 广播会挨个等所有顶层窗口，有一个无响应就卡满超时（单实例唤醒当年踩过这个坑）。
            SendMessageTimeout(
                progman,
                SpawnWorkerWMessage,
                IntPtr.Zero,
                IntPtr.Zero,
                SmtoAbortIfHung,
                SendMessageTimeoutMs,
                out _);
        }

        var bare = new List<(IntPtr Hwnd, long Area)>();
        var withIcons = new List<(IntPtr Hwnd, long Area)>();
        EnumWindows(
            (hwnd, _) =>
            {
                if (!string.Equals(ClassNameOf(hwnd), "WorkerW", StringComparison.Ordinal))
                {
                    return true;
                }

                // 隐藏或零尺寸的 WorkerW 挂上去也看不见，直接排除。
                if (!IsWindowVisible(hwnd) || !GetWindowRect(hwnd, out var rect))
                {
                    return true;
                }

                var area = (long)Math.Max(0, rect.Right - rect.Left) * Math.Max(0, rect.Bottom - rect.Top);
                if (area <= 0)
                {
                    return true;
                }

                var hasIcons = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero;
                (hasIcons ? withIcons : bare).Add((hwnd, area));
                return true;
            },
            IntPtr.Zero);

        bare.Sort((left, right) => right.Area.CompareTo(left.Area));
        withIcons.Sort((left, right) => right.Area.CompareTo(left.Area));

        var progmanHasIcons = progman != IntPtr.Zero
            && FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero;

        Log.Info(
            $"[探针] 候选：可见的无图标 WorkerW {bare.Count} 个，带图标 WorkerW {withIcons.Count} 个，"
            + $"Progman 0x{progman.ToInt64():X}（{(progmanHasIcons ? "图标挂在它下面" : "不带图标")}）。");

        var candidates = new List<IntPtr>();

        // 图标挂在 Progman 下面时，Progman 就是要进的那一层，排第一。
        if (progman != IntPtr.Zero && progmanHasIcons)
        {
            candidates.Add(progman);
        }

        candidates.AddRange(bare.Select(item => item.Hwnd));

        if (progman != IntPtr.Zero && !progmanHasIcons)
        {
            candidates.Add(progman);
        }

        candidates.AddRange(withIcons.Select(item => item.Hwnd));
        return candidates;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public static bool IsWallpaperEngineRunning()
    {
        foreach (var name in WallpaperEngineProcesses)
        {
            try
            {
                if (Process.GetProcessesByName(name).Length > 0)
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"[探针] 枚举进程 {name} 失败。", ex);
            }
        }

        return false;
    }

    /// <summary>
    /// 找那个"壁纸宿主"窗口：先催 Progman 生成 WorkerW，再挑**没有** <c>SHELLDLL_DefView</c>
    /// 子窗口的那个 WorkerW（有 DefView 的那个装着桌面图标，进去就跑到图标之上了）。
    /// 催生失败时退回 Progman 本身。
    /// </summary>
    public static IntPtr FindWallpaperHost()
    {
        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        // 定向发给 Progman 一个窗口，**不是** HWND_BROADCAST —— 广播会挨个等所有顶层窗口，
        // 有一个无响应就卡满超时（单实例唤醒当年就踩过这个坑）。
        SendMessageTimeout(
            progman,
            SpawnWorkerWMessage,
            IntPtr.Zero,
            IntPtr.Zero,
            SmtoAbortIfHung,
            SendMessageTimeoutMs,
            out _);

        var found = IntPtr.Zero;
        EnumWindows(
            (hwnd, _) =>
            {
                if (!string.Equals(ClassNameOf(hwnd), "WorkerW", StringComparison.Ordinal))
                {
                    return true;
                }

                if (FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                {
                    return true;
                }

                found = hwnd;
                return false;
            },
            IntPtr.Zero);

        return found != IntPtr.Zero ? found : progman;
    }

    private static string ClassNameOf(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        return GetClassName(hwnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : string.Empty;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hwnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        uint flags,
        uint timeoutMs,
        out IntPtr result);
}
