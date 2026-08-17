using System.Runtime.InteropServices;
using TransparentCalendar.Services;

namespace TransparentCalendar.Native;

/// <summary>
/// 窗口层级控制。
///
/// **默认路线不碰 WorkerW**：保持普通的分层透明窗口，需要"置底"时拦截 WM_WINDOWPOSCHANGING
/// 把 Z 序钉在最底 —— 这样窗口位于所有普通窗口之下、壁纸之上，且不影响键盘焦点。
///
/// <see cref="AttachToDesktop"/> 是**可选**的另一条路（设置里的「嵌入桌面」，默认关）：
/// SetParent 到 WorkerW 才能落到桌面**图标之下**。早年试过一次并放弃，因为
/// Wallpaper Engine 占的正是这一层，日历会被它整个盖住。`Native/DesktopLayerProbe`
/// 的探针（<c>--probe-desktop-layer</c>）在 Win11 上验过：挂载、半透明、键盘焦点三项都能保住，
/// 所以这条路保留为用户可选项，但 WE 在跑时要提醒用户可能看不见，并留好退回的路。
/// </summary>
public static class WindowLayerService
{
    public const int WmWindowPosChanging = 0x0046;

    private static readonly IntPtr HwndBottom = new(1);
    private const int SwpNoSize = 0x0001;
    private const int SwpNoMove = 0x0002;
    private const int SwpNoZOrder = 0x0004;
    private const int SwpNoActivate = 0x0010;
    private const uint GaParent = 1;

    /// <summary>把窗口立即压到 Z 序最底。</summary>
    public static void SendToBottom(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        SetWindowPos(hwnd, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    /// <summary>
    /// 在 WM_WINDOWPOSCHANGING 中改写 Z 序请求，使窗口始终回到最底。
    /// 单靠一次 SendToBottom 不够 —— 任何一次点击/激活都会把窗口重新抬起来。
    /// </summary>
    public static void ForceBottom(IntPtr lParam)
    {
        if (lParam == IntPtr.Zero)
        {
            return;
        }

        var position = Marshal.PtrToStructure<WindowPos>(lParam);
        position.HwndInsertAfter = HwndBottom;
        position.Flags &= ~SwpNoZOrder;
        Marshal.StructureToPtr(position, lParam, fDeleteOld: false);
    }

    /// <summary>
    /// 当前显示器布局是否支持嵌入桌面层。
    ///
    /// **只在虚拟桌面原点为 (0,0) 时支持。** 原因是坐标系冲突：挂进宿主后，窗口位置对 OS 而言是
    /// 相对宿主客户区的，而 WPF 记的是屏幕坐标，两者差一个宿主原点。实测（副屏在主屏左侧、
    /// 虚拟原点 -1920）WPF 会在**不发任何窗口位置消息**的情况下把位置改回它记的屏幕坐标，
    /// OS 又把那个值当相对坐标用 —— 每轮多偏 1920px，一次就把窗口甩出屏幕，拦不住也拦不干净。
    ///
    /// 原点为 (0,0) 时这个差值是 0，两套坐标天然一致，不会发生。
    /// 副屏在主屏左侧/上方的用户可以在 Windows 显示设置里把左上那台设为主显示器来绕开。
    /// </summary>
    public static bool IsDesktopLayerSupported(out int originLeft, out int originTop)
    {
        var host = DesktopLayerProbe.FindHostCandidates().FirstOrDefault();
        if (host == IntPtr.Zero)
        {
            originLeft = 0;
            originTop = 0;
            return false;
        }

        (originLeft, originTop, _, _) = ScreenRect(host);
        return originLeft == 0 && originTop == 0;
    }

    /// <summary>
    /// 把窗口挂到桌面层（Progman / WorkerW）之下，使其落在**桌面图标之下、壁纸之上**。
    /// 成功返回宿主句柄，调用方要用它换算窗口坐标（挂进去之后坐标是相对宿主客户区的）；
    /// 一个候选都挂不上就返回 <see cref="IntPtr.Zero"/>，须回退到普通层级。
    /// </summary>
    public static IntPtr AttachToDesktop(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        foreach (var host in DesktopLayerProbe.FindHostCandidates())
        {
            // 宿主原点必须是 (0,0)，否则挂进去必然错位，见 IsDesktopLayerSupported 的说明。
            var (hostLeft, hostTop, _, _) = ScreenRect(host);
            if (hostLeft != 0 || hostTop != 0)
            {
                Log.Warn(
                    $"宿主 0x{host.ToInt64():X} 的原点是 ({hostLeft},{hostTop}) 而非 (0,0)，"
                    + "嵌入桌面层会让窗口坐标错位，跳过。");
                continue;
            }

            SetParent(hwnd, host);

            // 回读必须用 GetAncestor：SetParent 成功时返回的是**原**父窗口（顶层窗口即 NULL），
            // GetParent 对非子窗口返回的又是 owner —— 两者都判不出成败。
            if (GetAncestor(hwnd, GaParent) != host)
            {
                continue;
            }

            // Z 序要**夹在两者之间**，不能简单压到最底：
            // Progman 底下既有 SHELLDLL_DefView（桌面图标），又有一个画壁纸的 WorkerW，
            // 而壁纸那个在图标之下。压到最底就落到壁纸之下，被壁纸整块盖住 ——
            // 这正是当年"SetParent 之后完全看不见"的真相，不是 Wallpaper Engine 独有的问题。
            var iconView = FindWindowEx(host, IntPtr.Zero, "SHELLDLL_DefView", null);
            var insertAfter = iconView != IntPtr.Zero ? iconView : HwndBottom;
            SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);

            Log.Info(
                $"已嵌入桌面层，宿主 0x{host.ToInt64():X}，"
                + (iconView != IntPtr.Zero
                    ? $"插在桌面图标 0x{iconView.ToInt64():X} 之下。"
                    : "宿主下没有找到桌面图标窗口，退而压到最底。"));
            return host;
        }

        Log.Warn("无法嵌入桌面层（没有可用的 Progman / WorkerW 宿主），保持普通窗口。");
        return IntPtr.Zero;
    }

    /// <summary>
    /// 在宿主客户区内摆放窗口（**设备像素**，相对宿主客户区左上角）。
    ///
    /// 桌面层里不要用 WPF 的 <c>Left</c>/<c>Top</c>：`Show()` 收尾时 WPF 会按自己记的
    /// 屏幕坐标再摆一次，把 `SourceInitialized` 里算好的相对坐标盖掉（实测窗口会飞到
    /// 虚拟屏幕之外）。直接 SetWindowPos 绕开这本坐标簿。
    /// </summary>
    public static void MoveWithinHost(IntPtr hwnd, int x, int y, int width, int height)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, SwpNoZOrder | SwpNoActivate);
    }

    /// <summary>窗口的屏幕矩形（设备像素）。子窗口也返回屏幕坐标，用来把桌面层里的位置换算回去。</summary>
    public static (int Left, int Top, int Width, int Height) ScreenRect(IntPtr hwnd)
    {
        return GetWindowRect(hwnd, out var rect)
            ? (rect.Left, rect.Top, Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top))
            : (0, 0, 0, 0);
    }

    /// <summary>
    /// 从桌面层摘回来，恢复成普通的顶层分层窗口。
    /// 调用方随后要重新施加 WS_EX_TOOLWINDOW 并复位窗口位置 ——
    /// 挂进去之后坐标是相对宿主的，摘出来要重新按屏幕坐标摆一次。
    /// </summary>
    public static void DetachFromDesktop(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        SetParent(hwnd, IntPtr.Zero);
        Log.Info("已从桌面层摘回普通窗口。");
    }

    /// <summary>
    /// 在 <c>WM_WINDOWPOSCHANGING</c> 里把 Z 序请求改成"紧贴在 <paramref name="insertAfter"/> 之下"。
    /// 桌面层用它把自己钉在桌面图标之下、壁纸之上 —— 单次 SetWindowPos 会被任何一次激活抬回去。
    /// </summary>
    public static void ForceInsertAfter(IntPtr lParam, IntPtr insertAfter)
    {
        if (lParam == IntPtr.Zero || insertAfter == IntPtr.Zero)
        {
            return;
        }

        var position = Marshal.PtrToStructure<WindowPos>(lParam);
        position.HwndInsertAfter = insertAfter;
        position.Flags &= ~SwpNoZOrder;
        Marshal.StructureToPtr(position, lParam, fDeleteOld: false);
    }

    /// <summary>宿主下的桌面图标窗口（<c>SHELLDLL_DefView</c>）；没有就返回 <see cref="IntPtr.Zero"/>。</summary>
    public static IntPtr FindDesktopIconView(IntPtr host)
    {
        return host == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(host, IntPtr.Zero, "SHELLDLL_DefView", null);
    }

    /// <summary>读出 <c>WM_WINDOWPOSCHANGING</c> 里请求的新位置；<c>Moves</c> 为 false 表示这次不动位置。</summary>
    public static (bool Moves, int X, int Y) ReadRequestedPosition(IntPtr lParam)
    {
        if (lParam == IntPtr.Zero)
        {
            return (false, 0, 0);
        }

        var position = Marshal.PtrToStructure<WindowPos>(lParam);
        return ((position.Flags & SwpNoMove) == 0, position.X, position.Y);
    }

    /// <summary>改写 <c>WM_WINDOWPOSCHANGING</c> 里的目标位置。</summary>
    public static void OverrideRequestedPosition(IntPtr lParam, int x, int y)
    {
        if (lParam == IntPtr.Zero)
        {
            return;
        }

        var position = Marshal.PtrToStructure<WindowPos>(lParam);
        position.X = x;
        position.Y = y;
        position.Flags &= ~SwpNoMove;
        Marshal.StructureToPtr(position, lParam, fDeleteOld: false);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public IntPtr Hwnd;
        public IntPtr HwndInsertAfter;
        public int X;
        public int Y;
        public int Cx;
        public int Cy;
        public int Flags;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        int uFlags);
}
