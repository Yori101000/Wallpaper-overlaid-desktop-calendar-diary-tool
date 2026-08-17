using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using TransparentCalendar.Models;
using TransparentCalendar.Native;
using TransparentCalendar.Services;
using TransparentCalendar.Views;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfCursors = System.Windows.Input.Cursors;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfPoint = System.Windows.Point;
using WpfTypography = System.Windows.Documents.Typography;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;
using static TransparentCalendar.Models.CalendarQuery;
using static TransparentCalendar.Models.DateKeys;

namespace TransparentCalendar;

// 窗口外壳：消息钩子、窗口层级、托盘、拖动、设置往返、Win32 互操作。
public partial class MainWindow : Window
{
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // 置底：任何一次激活都会把窗口抬起来，必须在每次 Z 序变化时重新压回底部。
        //
        // 「嵌入桌面」同样需要这一手，而且理由更硬：那时窗口是 Progman 的子窗口，
        // 桌面图标（SHELLDLL_DefView）是它的兄弟 —— 压到兄弟里的最底才叫"在图标之下"。
        // 实测只在挂载时 SendToBottom 一次是不够的，窗口会回到 DefView 之上。
        if (msg == WindowLayerService.WmWindowPosChanging
            && string.Equals(_settings.WindowLayer, WindowLayers.Bottom, StringComparison.Ordinal))
        {
            WindowLayerService.ForceBottom(lParam);
        }

        if (msg == WindowLayerService.WmWindowPosChanging && _desktopHost != IntPtr.Zero)
        {
            // 桌面层不能用 ForceBottom：宿主下面还有一个画壁纸的 WorkerW，压到最底就被壁纸盖住。
            // 要钉在桌面图标**之下**、壁纸**之上**。同样只钉一次不够，任何一次激活都会抬回去。
            WindowLayerService.ForceInsertAfter(lParam, _desktopIconView);
            KeepInsideDesktopHost(lParam);
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// 挡掉桌面层里的"坐标漂移"。
    ///
    /// 挂进宿主之后，窗口位置对 OS 而言是**相对宿主客户区**的，而 WPF 记的是**屏幕坐标**：
    /// 它每次重摆窗口都会多偏一个宿主原点。虚拟屏幕原点为负时（副屏在左侧，实测 -1920）
    /// 一次就能把窗口甩到屏幕外，表现为"开了嵌入桌面就整个看不见"。
    ///
    /// 判据用"是否越出宿主范围"：WPF 的漂移值必然出界，用户拖动给的值必然在界内 ——
    /// 出界就按上一个好位置改回去，在界内就认下来，拖动因此照常可用。
    /// </summary>
    private void KeepInsideDesktopHost(IntPtr lParam)
    {
        var (moves, x, y) = WindowLayerService.ReadRequestedPosition(lParam);
        if (!moves)
        {
            return;
        }

        var (_, _, hostWidth, hostHeight) = WindowLayerService.ScreenRect(_desktopHost);
        if (hostWidth <= 0 || hostHeight <= 0)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var width = (int)Math.Round(Width * dpi.DpiScaleX);
        var height = (int)Math.Round(Height * dpi.DpiScaleY);
        var inside = x >= 0 && y >= 0 && x <= Math.Max(0, hostWidth - width) && y <= Math.Max(0, hostHeight - height);

        if (inside)
        {
            _desktopX = x;
            _desktopY = y;
            return;
        }

        WindowLayerService.OverrideRequestedPosition(lParam, _desktopX, _desktopY);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        OpenSettings();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.CloseToTray)
        {
            HideWindowToTray();
            return;
        }

        ExitApplication();
    }

    /// <summary>
    /// 全窗口快捷键。绑在 PreviewKeyDown 上以便在焦点落在按钮时也生效；
    /// 但焦点在文本框里时只处理 Esc，否则会吞掉正常输入。
    /// </summary>
    private void MainWindow_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        var inTextBox = Keyboard.FocusedElement is System.Windows.Controls.TextBox;

        if (e.Key == Key.Escape)
        {
            // 搜索框里按 Esc 先清空搜索，再按才收起窗口。
            if (!string.IsNullOrEmpty(SearchTextBox.Text))
            {
                SearchTextBox.Text = string.Empty;
                Keyboard.ClearFocus();
                e.Handled = true;
                return;
            }

            if (_settings.CloseToTray)
            {
                HideWindowToTray();
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            SearchTextBox.Focus();
            SearchTextBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (inTextBox || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Left:
                PreviousMonth_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.Right:
                NextMonth_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.T:
            case Key.Home:
                GoToToday();
                e.Handled = true;
                break;
        }
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_settings.IsLocked || e.ChangedButton != MouseButton.Left || IsInteractiveDragSource(e.OriginalSource))
        {
            return;
        }

        TryDragMove();
    }

    private void TryDragMove()
    {
        try
        {
            DragMove();
        }
        catch
        {
            // DragMove can throw when the pointer is released during startup or modal transitions.
        }
    }

    private static bool IsInteractiveDragSource(object source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is WpfButton
                or System.Windows.Controls.TextBox
                or System.Windows.Controls.CheckBox
                or System.Windows.Controls.ComboBox
                or System.Windows.Controls.Primitives.ScrollBar
                or Slider)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        SaveWindowSettings();
        if (_isExitRequested)
        {
            return;
        }

        if (_settings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void OpenSettings()
    {
        SaveWindowSettings();
        if (!IsVisible)
        {
            ShowWindowFromTray();
        }

        var settingsWindow = new SettingsWindow(_settings, _storage, _entries)
        {
            Owner = this
        };

        var accepted = settingsWindow.ShowDialog() == true;
        if (!accepted && !settingsWindow.DataImported)
        {
            return;
        }

        _settings = settingsWindow.Settings;
        if (accepted)
        {
            _storage.SaveSettings(_settings);
        }

        ApplySettings();
        RenderCalendar();
        ConfirmDesktopLayer();
        ApplyWindowLayer();
    }

    private void SaveWindowSettings()
    {
        // 嵌在桌面层时 WPF 的 Left/Top 已经不是屏幕坐标了（我们直接 SetWindowPos 摆的），
        // 所以回读真实窗口矩形再换算 —— 否则下次以普通层级启动会摆到别的地方去。
        if (_desktopHost != IntPtr.Zero)
        {
            var (screenLeft, screenTop, _, _) =
                WindowLayerService.ScreenRect(new WindowInteropHelper(this).Handle);
            var dpi = VisualTreeHelper.GetDpi(this);
            _settings.Left = screenLeft / dpi.DpiScaleX;
            _settings.Top = screenTop / dpi.DpiScaleY;
        }
        else
        {
            _settings.Left = Left;
            _settings.Top = Top;
        }

        _settings.Width = Width;
        _settings.Height = Height;
        _storage.SaveSettings(_settings);
    }

    private void ApplyStartupVisibility()
    {
        if (_settings.StartInTray)
        {
            HideWindowToTray();
            return;
        }

        BringIntoViewOnce();
    }

    private void BringIntoViewOnce()
    {
        // 置底模式下不要抢焦点，否则会与"钉在最底"的规则来回打架。
        // 桌面层同理：那时窗口是 WorkerW 的子窗口，激活它只会把桌面本身提到前面。
        if (string.Equals(_settings.WindowLayer, WindowLayers.Bottom, StringComparison.Ordinal)
            || string.Equals(_settings.WindowLayer, WindowLayers.Desktop, StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(_settings.WindowLayer, WindowLayers.Top, StringComparison.Ordinal))
        {
            Activate();
            return;
        }

        Topmost = true;
        Activate();
        Topmost = false;
    }

    private void ApplyWindowLayer()
    {
        var layer = _settings.WindowLayer;
        var wantsDesktop = string.Equals(layer, WindowLayers.Desktop, StringComparison.Ordinal);
        Topmost = string.Equals(layer, WindowLayers.Top, StringComparison.Ordinal);

        var hwnd = new WindowInteropHelper(this).Handle;

        // 桌面层与其他三种层级互斥：切走时必须先摘回来，否则窗口还挂在 WorkerW 下面，
        // 用户会以为设置没生效。
        if (_desktopHost != IntPtr.Zero && !wantsDesktop)
        {
            WindowLayerService.DetachFromDesktop(hwnd);
            _desktopHost = IntPtr.Zero;
            _desktopIconView = IntPtr.Zero;
            RestoreWindowPosition();
        }

        if (wantsDesktop && _desktopHost == IntPtr.Zero)
        {
            _desktopHost = WindowLayerService.AttachToDesktop(hwnd);
            _desktopIconView = WindowLayerService.FindDesktopIconView(_desktopHost);
            if (_desktopHost == IntPtr.Zero)
            {
                // 挂不上就老实退回普通层级并落盘，避免每次启动都白试一遍。
                _settings.WindowLayer = WindowLayers.Normal;
                _storage.SaveSettings(_settings);
            }
            else
            {
                // 挂进去之后坐标变成相对宿主客户区的，必须重新摆一次 ——
                // 副屏上的负坐标（Left=-1864）会直接落到宿主之外被裁掉，表现为"整个看不见"。
                RestoreWindowPosition();
            }
        }

        if (string.Equals(_settings.WindowLayer, WindowLayers.Bottom, StringComparison.Ordinal))
        {
            WindowLayerService.SendToBottom(hwnd);
        }

        UpdateDesktopWatchdog();
        HideMainWindowFromFastSwitcher();
    }

    /// <summary>
    /// 只在嵌入桌面时开着的位置看守。挪动窗口的那次操作**不经过** <c>WM_WINDOWPOSCHANGING</c>，
    /// <c>LocationChanged</c> 也未必触发（WPF 自己都不知道窗口被挪了），只能定期回读实际矩形。
    /// 每秒两个 GetWindowRect，代价可忽略。
    /// </summary>
    private void UpdateDesktopWatchdog()
    {
        if (_desktopHost == IntPtr.Zero)
        {
            _desktopWatchdog?.Stop();
            return;
        }

        if (_desktopWatchdog is null)
        {
            _desktopWatchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _desktopWatchdog.Tick += (_, _) => EnsureInsideDesktopHost();
        }

        _desktopWatchdog.Start();
    }

    /// <summary>
    /// 按设置里的屏幕坐标摆回窗口。嵌在桌面层时要减去宿主的左上角 ——
    /// 那时 <see cref="Window.Left"/> 是相对宿主客户区的，直接写屏幕坐标会跑到宿主外面被裁掉。
    /// </summary>
    private void RestoreWindowPosition()
    {
        if (_desktopHost == IntPtr.Zero)
        {
            Left = _settings.Left;
            Top = _settings.Top;
            return;
        }

        // WPF 会在 Show() 收尾时按自己记的坐标再摆一次，所以这里推到 Loaded 优先级之后再摆，
        // 并且直接走 SetWindowPos（相对宿主客户区、设备像素），不碰 Left/Top。
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, PlaceInDesktopHost);
    }

    /// <summary>
    /// 桌面层的位置自愈。
    ///
    /// 挂进宿主后，窗口位置对 OS 是**相对宿主客户区**的，而 WPF 记的是**屏幕坐标**，两者差一个
    /// 宿主原点（虚拟屏幕原点为负时就是 -1920）。WPF 在 Show() 之后还会按自己的账本再摆一次，
    /// 而且实测这一次**不经过** <c>WM_WINDOWPOSCHANGING</c>（钩子里收不到），拦不住 ——
    /// 所以改为事后检查：位置一旦落到宿主之外，就用 SetWindowPos 摆回上一个好位置。
    /// </summary>
    private void EnsureInsideDesktopHost()
    {
        if (_desktopHost == IntPtr.Zero || _isFixingDesktopPosition)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        var (hostLeft, hostTop, hostWidth, hostHeight) = WindowLayerService.ScreenRect(_desktopHost);
        var (screenLeft, screenTop, width, height) = WindowLayerService.ScreenRect(hwnd);
        if (hostWidth <= 0 || hostHeight <= 0 || width <= 0)
        {
            return;
        }

        var relativeX = screenLeft - hostLeft;
        var relativeY = screenTop - hostTop;
        if (relativeX >= 0 && relativeY >= 0
            && relativeX <= hostWidth - width && relativeY <= hostHeight - height)
        {
            // 位置合法（含用户自己拖到的新位置）：记下来当作下一次自愈的锚点。
            _desktopX = relativeX;
            _desktopY = relativeY;
            return;
        }

        _isFixingDesktopPosition = true;
        try
        {
            WindowLayerService.MoveWithinHost(hwnd, _desktopX, _desktopY, width, height);
            if (_desktopMoveLogCount < DesktopMoveLogLimit)
            {
                _desktopMoveLogCount++;
                Log.Info($"桌面层位置漂到宿主之外（{screenLeft},{screenTop}），已摆回 ({_desktopX},{_desktopY})。");
            }
        }
        finally
        {
            _isFixingDesktopPosition = false;
        }
    }

    private void PlaceInDesktopHost()
    {
        if (_desktopHost == IntPtr.Zero)
        {
            return;
        }

        var (hostLeft, hostTop, hostWidth, hostHeight) = WindowLayerService.ScreenRect(_desktopHost);
        if (hostWidth <= 0 || hostHeight <= 0)
        {
            Log.Warn($"桌面层宿主 0x{_desktopHost.ToInt64():X} 取不到矩形，位置保持不变。");
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var width = (int)Math.Round(Width * dpi.DpiScaleX);
        var height = (int)Math.Round(Height * dpi.DpiScaleY);

        // 屏幕坐标 → 相对宿主客户区，再夹进宿主范围（超出去会被父窗口裁掉，表现为整个看不见）
        var x = (int)Math.Round(_settings.Left * dpi.DpiScaleX) - hostLeft;
        var y = (int)Math.Round(_settings.Top * dpi.DpiScaleY) - hostTop;
        var clampedX = Math.Clamp(x, 0, Math.Max(0, hostWidth - width));
        var clampedY = Math.Clamp(y, 0, Math.Max(0, hostHeight - height));

        if (clampedX != x || clampedY != y)
        {
            Log.Info($"桌面层宿主 {hostWidth}×{hostHeight} 放不下原位置，已从 ({x},{y}) 移到 ({clampedX},{clampedY})。");
        }

        // 先记下目标位置：漂移自愈要拿它当"上一个好位置"。
        _desktopX = clampedX;
        _desktopY = clampedY;

        // 只用 SetWindowPos，**绝不碰 WPF 的 Left/Top**：
        // 桌面层里 OS 用宿主相对坐标，而 WPF 记的是回读到的屏幕坐标，一旦让它参与就会
        // 每轮多偏一个宿主原点（虚拟屏幕原点为负时一次就甩出屏幕）。
        WindowLayerService.MoveWithinHost(
            new WindowInteropHelper(this).Handle, clampedX, clampedY, width, height);

        var (afterLeft, afterTop, _, _) =
            WindowLayerService.ScreenRect(new WindowInteropHelper(this).Handle);
        Log.Info(
            $"桌面层摆位：宿主 {hostLeft},{hostTop} {hostWidth}×{hostHeight}，"
            + $"相对坐标 ({clampedX},{clampedY})，实际屏幕坐标 = {afterLeft},{afterTop}。");
    }

    /// <summary>
    /// 选「嵌入桌面」而 Wallpaper Engine 正在跑时先打个招呼 ——
    /// 两者抢同一层，日历很可能直接看不见。让用户自己决定要不要试，并告诉他怎么退回来。
    /// </summary>
    private bool ConfirmDesktopLayer()
    {
        if (!string.Equals(_settings.WindowLayer, WindowLayers.Desktop, StringComparison.Ordinal))
        {
            return true;
        }

        // 显示器布局不支持就直接说清原因并退回，不要让用户对着一个消失的日历猜。
        if (!WindowLayerService.IsDesktopLayerSupported(out var originLeft, out var originTop))
        {
            WpfMessageBox.Show(
                this,
                $"当前显示器布局不支持「嵌入桌面」。\n\n"
                + $"桌面窗口的原点是 ({originLeft}, {originTop}) 而不是 (0, 0) —— "
                + "通常是因为有一台显示器排在主显示器的左侧或上方。这种布局下嵌入桌面层会让日历的"
                + "坐标错位、直接跑到屏幕外面。\n\n"
                + "想用这个功能，可以在 Windows 显示设置里把最左上那台显示器设为主显示器。\n\n"
                + "已改回「普通」层级。",
                "透明日历",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            _settings.WindowLayer = WindowLayers.Normal;
            _storage.SaveSettings(_settings);
            return false;
        }

        if (!DesktopLayerProbe.IsWallpaperEngineRunning())
        {
            return true;
        }

        var choice = WpfMessageBox.Show(
            this,
            "检测到 Wallpaper Engine 正在运行。\n\n"
            + "「嵌入桌面」会把日历挂到桌面图标之下，而这一层正是 Wallpaper Engine 占用的 —— "
            + "日历有可能被壁纸整个盖住、完全看不见。\n\n"
            + "仍要试试吗？看不见时可以在托盘图标右键里选「恢复为普通窗口」退回来。",
            "透明日历",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (choice == MessageBoxResult.Yes)
        {
            return true;
        }

        _settings.WindowLayer = WindowLayers.Normal;
        _storage.SaveSettings(_settings);
        return false;
    }

    /// <summary>托盘里的救命退路：桌面层下万一看不见，用它退回普通窗口。</summary>
    private void RestoreToNormalLayer()
    {
        _settings.WindowLayer = WindowLayers.Normal;
        _storage.SaveSettings(_settings);
        ApplyWindowLayer();

        if (!IsVisible)
        {
            ShowWindowFromTray();
            return;
        }

        Activate();
    }

    private void HideMainWindowFromFastSwitcher()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var style = GetWindowLongPtrSafe(hwnd, GwlExStyle).ToInt64();
        style &= ~WsExAppWindow;
        style |= WsExToolWindow;
        SetWindowLongPtrSafe(hwnd, GwlExStyle, new IntPtr(style));
        SetWindowPos(
            hwnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    private void InitializeTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示", null, (_, _) => Dispatcher.Invoke(ShowWindowFromTray));
        menu.Items.Add("隐藏", null, (_, _) => Dispatcher.Invoke(HideWindowToTray));
        menu.Items.Add("设置", null, (_, _) => Dispatcher.Invoke(OpenSettings));
        // 桌面层看不见时的退路。必须放在托盘里 —— 那种情况下窗口本身点不到。
        menu.Items.Add("恢复为普通窗口", null, (_, _) => Dispatcher.Invoke(RestoreToNormalLayer));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        _trayIcon = new Forms.NotifyIcon
        {
            Text = "透明日历",
            Visible = true,
            ContextMenuStrip = menu
        };
        UpdateTrayIcon();
        _trayIcon.MouseDoubleClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
            {
                Dispatcher.Invoke(ToggleWindowVisibility);
            }
        };
    }

    private void ToggleWindowVisibility()
    {
        if (IsVisible)
        {
            HideWindowToTray();
            return;
        }

        ShowWindowFromTray();
    }

    private void ShowWindowFromTray()
    {
        Show();
        HideMainWindowFromFastSwitcher();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        ApplyWindowLayer();
        Activate();
    }

    private void HideWindowToTray()
    {
        SaveWindowSettings();
        Hide();
    }

    private void ExitApplication()
    {
        _isExitRequested = true;
        Close();
    }

    /// <summary>托盘图标画的是当日日期，跨天时随每日刷新定时器一起更新。</summary>
    private void UpdateTrayIcon()
    {
        if (_trayIcon is null)
        {
            return;
        }

        try
        {
            var previous = _trayIcon.Icon;
            _trayIcon.Icon = AppIcon.CreateTrayIcon(DateTime.Today.Day);
            previous?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn("托盘图标生成失败，回退为系统默认图标。", ex);
            _trayIcon.Icon ??= Drawing.SystemIcons.Application;
        }
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon is null)
        {
            return;
        }

        _trayIcon.Visible = false;
        var icon = _trayIcon.Icon;
        _trayIcon.Dispose();
        icon?.Dispose();
        _trayIcon = null;
    }

    private void CreateStartupBackup()
    {
        try
        {
            // 当天已有备份就跳过：否则连开十次应用就会把全部历史备份冲掉。
            _storage.CreateAutomaticBackup(_settings, _entries, force: false);
        }
        catch (Exception ex)
        {
            // 备份失败不能阻止日历打开，但必须留下痕迹。
            Log.Error("启动备份失败。", ex);
        }
    }

    private static IntPtr GetWindowLongPtrSafe(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr(hWnd, nIndex)
            : new IntPtr(GetWindowLong(hWnd, nIndex));
    }

    private static IntPtr SetWindowLongPtrSafe(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr(hWnd, nIndex, dwNewLong)
            : new IntPtr(SetWindowLong(hWnd, nIndex, dwNewLong.ToInt32()));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

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
