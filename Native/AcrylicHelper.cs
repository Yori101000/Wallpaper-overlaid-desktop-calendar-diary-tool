using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using TransparentCalendar.Services;

namespace TransparentCalendar.Native;

/// <summary>
/// 亚克力底垫层窗口：完全透明、点击穿透、不激活的顶层窗口，钉在主窗口正下方，
/// 由 DWM 画"它身后"的背景材质（Win11 = 半透明模糊，Win10 = 半透明 tint，实测结论见常量注释）。
///
/// 用原生 Win32 窗口而不是 WPF Window：WPF 会在每次风格变更时按自己的属性整体重写
/// exstyle，外部设的 WS_EX_NOACTIVATE / WS_EX_TOOLWINDOW 会被立刻抹掉（实测从别的进程
/// 设也留不住），Alt-Tab 里会多出一个幽灵窗。原生窗口自己说了算。
///
/// 为什么不能自己采样屏幕：面板盖住了自己的背景，从屏幕抓面板后方区域拿到的是
/// 面板上一帧（模糊图 + tint），再显示回去形成正反馈——几百帧内收敛成
/// "全不透明 tint 色"，壁纸一点光都透不进来。DWM backdrop 画的是垫层身后的内容，
/// 不含它自己，没有反馈回路。
/// </summary>
public sealed class AcrylicHelper : IDisposable
{
    private const int GwlExStyle = -20;
    private const int GwlWndProc = -4;
    private const uint WsExNoActivate = 0x08000000;
    private const uint WsExToolWindow = 0x00800000;
    private const uint WsExTransparent = 0x00000020;
    private const uint WsExTopmost = 0x00000008;
    private const uint WsPopup = 0x80000000;

    private const uint WmPaint = 0x000F;
    private const uint WmEraseBkgnd = 0x0014;
    private const uint WmWindowPosChanging = 0x0046;
    private const int SwpNoSize = 0x0001;
    private const int SwpNoMove = 0x0002;
    private const int SwpNoZOrder = 0x0004;
    private const int SwpNoActivate = 0x0010;
    private const int SwShowNa = 4;
    private const int SwHide = 0;

    private const int DwmwaBlurBehind = 33;
    private const int DwmwaSystemBackdropType = 38;
    // 本机实测（Win10 25H2 Insider）：2/4 渲染成不透明平色（就是"亚克力不透光"），
    // 0/1 全透明，5（MICA_TRANSITIVITY）半透明、壁纸透光带淡 tint。
    // Win11 上 2（TABBED）是真正的半透明模糊材质。
    private const int DwmSystemBackdropTypeTabbed = 2;
    private const int DwmSystemBackdropTypeMicaTransit = 5;

    private static readonly bool s_systemBackdropIsTranslucent = IsTranslucentSystemBackdrop();

    private static int _instanceCount;
    private static readonly object _lock = new();
    private static readonly Dictionary<IntPtr, AcrylicHelper> _byHwnd = new();
    private static readonly WndProcDelegate _wndProcDelegate = StaticWndProc;
    private static readonly IntPtr _wndProcPtr = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);

    private IntPtr _hwnd;
    private IntPtr _mainHwnd;
    private bool _pinning;
    private bool _disposed;
    private (int X, int Y, int W, int H) _lastRect = (-1, -1, -1, -1);
    private bool? _lastVisible;
    private bool? _lastTopmost;
    public AcrylicHelper()
    {
        _instanceCount++;
        Log.Warn($"[acrylic] helper #{_instanceCount} creating");

        // 用系统 STATIC 类建窗：本机新注册类建窗一律 1407（DefWindowProcW 探针也不通），
        // 只有系统类可用。空文本 STATIC 不画任何东西，建窗后再换上自己的 WndProc 接管擦除/绘制。
        // 屏外 1x1 创建，首帧就绪后再 Move 过去，避免一闪。
        _hwnd = CreateWindowExW(WsExTransparent, "STATIC", "", WsPopup, -100, -100, 1, 1, IntPtr.Zero, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            Log.Error($"[acrylic] CreateWindowEx failed err={Marshal.GetLastWin32Error()}");
            return;
        }
        Log.Warn($"[acrylic] static create hwnd=0x{_hwnd:X}");

        // TOOLWINDOW/NOACTIVATE 作为建窗参数在本机被拒（87），建窗后用 SetWindowLongPtr 补上
        //（主窗口 HideMainWindowFromFastSwitcher 走的就是这条路）。
        var ex = (uint)GetWindowLongPtr(_hwnd, GwlExStyle);
        SetWindowLongPtr(_hwnd, GwlExStyle, ex | WsExToolWindow | WsExNoActivate);
        Log.Warn($"[acrylic] exstyle 0x{ex:X} -> 0x{(uint)GetWindowLongPtr(_hwnd, GwlExStyle):X}");
        // 换自己的 WndProc：永不擦除、永不绘制（DWM 表面保持透明让 backdrop 透出来），
        // 并拦 WM_WINDOWPOSCHANGING 把 Z 序钉回主窗口正下方。
        var oldProc = SetWindowLongPtr(_hwnd, GwlWndProc, _wndProcPtr.ToInt64());
        Log.Warn($"[acrylic] wndproc swapped old=0x{oldProc:X}");

        lock (_lock)
        {
            _byHwnd[_hwnd] = this;
        }

        // Win11：TABBED（2）半透明模糊；Win10：2 渲染成不透明平色（实测），改用 5 透光；系统不支持该属性时回退 BlurBehind。
        var type = s_systemBackdropIsTranslucent ? DwmSystemBackdropTypeTabbed : DwmSystemBackdropTypeMicaTransit;
        var hr = DwmSetWindowAttribute(_hwnd, DwmwaSystemBackdropType, ref type, sizeof(int));
        Log.Warn($"[acrylic] DWMWA_SYSTEMBACKDROP_TYPE(38) type={type} hr=0x{hr:X8}");
        if (hr != 0)
        {
            int on = 1;
            var hr2 = DwmSetWindowAttribute(_hwnd, DwmwaBlurBehind, ref on, sizeof(int));
            Log.Warn($"[acrylic] DWMWA_BLURBEHIND(33) hr=0x{hr2:X8}");
        }
    }
    /// <summary>记住主窗口，并把垫层钉在它正下方。</summary>
    public void AttachToMain(IntPtr mainHwnd)
    {
        _mainHwnd = mainHwnd;
        PinBelowMain("attach");
    }

    /// <summary>置顶模式下垫层必须一起置顶，否则会被普通窗口压住。</summary>
    public void SetTopmost(bool topmost)
    {
        if (_hwnd == IntPtr.Zero || _disposed || _lastTopmost == topmost)
        {
            return;
        }

        _lastTopmost = topmost;
        var ex = (uint)GetWindowLongPtr(_hwnd, GwlExStyle);
        SetWindowLongPtr(_hwnd, GwlExStyle, topmost ? ex | WsExTopmost : ex & ~WsExTopmost);
        Log.Warn($"[acrylic] topmost={topmost} exstyle=0x{(uint)GetWindowLongPtr(_hwnd, GwlExStyle):X}");
        PinBelowMain("topmost");
    }
    /// <summary>
    /// 移动垫层（物理像素）。内缩 3px：面板 14px 圆角在斜向最多凹进约 5.4px，
    /// 垫层的直角会被面板实色 tint 盖住，不露方角糊边。
    /// </summary>
    public void Move(int left, int top, int width, int height)
    {
        if (_hwnd == IntPtr.Zero || _disposed)
        {
            return;
        }

        var rect = (left, top, width, height);
        if (rect == _lastRect)
        {
            return;
        }

        _lastRect = rect;
        SetWindowPos(_hwnd, IntPtr.Zero, left, top, width, height, SwpNoZOrder | SwpNoActivate);
        Log.Warn($"[acrylic] move {left},{top} {width}x{height}");
        PinBelowMain("move");
    }

    public void SetVisible(bool visible)
    {
        if (_hwnd == IntPtr.Zero || _disposed || _lastVisible == visible)
        {
            return;
        }

        _lastVisible = visible;
        Log.Warn($"[acrylic] visible={visible}");
        ShowWindow(_hwnd, visible ? SwShowNa : SwHide);
    }
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Log.Warn($"[acrylic] helper disposed");
        lock (_lock)
        {
            _byHwnd.Remove(_hwnd);
        }

        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
        }
    }

    /// <summary>把垫层强制钉回主窗口正下方（幂等）。</summary>
    private void PinBelowMain(string reason)
    {
        if (_hwnd == IntPtr.Zero || _mainHwnd == IntPtr.Zero || _disposed || _pinning)
        {
            return;
        }

        _pinning = true;
        try
        {
            var ok = SetWindowPos(_hwnd, _mainHwnd, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate);
            if (reason != "watch")
            {
                Log.Warn($"[acrylic] pin({reason}) ok={ok}");
            }
        }
        finally
        {
            _pinning = false;
        }
    }
    private static IntPtr StaticWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // 永远不画、不擦：DWM 表面保持透明，backdrop 材质才能透出来。
        if (msg == WmEraseBkgnd)
        {
            return (IntPtr)1;
        }

        if (msg == WmPaint)
        {
            ValidateRect(hwnd, IntPtr.Zero);
            return IntPtr.Zero;
        }

        AcrylicHelper? self = null;
        lock (_lock)
        {
            _byHwnd.TryGetValue(hwnd, out self);
        }

        if (self is not null && msg == WmWindowPosChanging && !self._pinning)
        {
            Log.Warn("[acrylic] z-order disturbed, repinning");
            self.PinBelowMain("wndproc");
        }

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }
    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Win11 用 TABBED（2，半透明模糊）；Win10 的 2 会渲染成不透明平色（实测），改用 5（半透明 tint）。
    /// 按注册表 ProductName 判断 —— build 号分不出 Insider 线（Win10 Insider 同样是 26200）。
    /// 读失败保守返回 false：5 在 Win10/Win11 都是半透明，不会退回"不透光"。
    /// </summary>
    private static bool IsTranslucentSystemBackdrop()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var name = key?.GetValue("ProductName") as string ?? string.Empty;
            return name.Contains("Windows 11", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern long GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern long SetWindowLongPtr(IntPtr hwnd, int index, long value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, int flags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int cmdShow);

    [DllImport("user32.dll")]
    private static extern bool ValidateRect(IntPtr hwnd, IntPtr lpRect);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW([System.Diagnostics.CodeAnalysis.AllowNull] string name);
}