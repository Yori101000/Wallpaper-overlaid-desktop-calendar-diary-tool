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

public partial class MainWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExAppWindow = 0x00040000;
    private const int WsExToolWindow = 0x00000080;
    private const int SwpNoSize = 0x0001;
    private const int SwpNoMove = 0x0002;
    private const int SwpNoZOrder = 0x0004;
    private const int SwpNoActivate = 0x0010;
    private const int SwpFrameChanged = 0x0020;
    private const int DayCellCount = 42;

    // ── 顶栏的窄窗口降级 ──
    // 顶栏一行要塞下月份导航、今天、三个模式、搜索、设置、关闭，窗口可以被拖到 480px。
    // WPF 没有媒体查询，只能按 ActualWidth 分档。降级顺序是刻意的：**先砍标题的年份**
    // （信息冗余度最高，翻月份时年份基本不变），再砍模式按钮的文字。
    private const double CompactTitleWidth = 560;
    private const double CompactAllWidth = 500;
    private const int SearchDebounceMs = 200;
    private const int UrgencyGroupPreviewCount = 10;
    private const double AdjacentMonthOpacity = 0.28;
    private const double WeekHeaderFontSize = 11;

    // ── 日期格的三行定高（相对基准字号）──
    // 定高是为了让数字的垂直位置与格子里有没有内容**无关**：原先用居中 StackPanel，
    // 有待办的格子数字被顶高、空格子偏低，整行数字高低不齐。
    private const double NumberRowRatio = 1.25;
    private const double AlmanacRowRatio = 1.35;
    private const double MarkerRowHeight = 7;
    private const double DayBadgeSize = 14;

    /// <summary>徽章相对格子中心右移多少（× 基准字号）—— 让它咬住自己的数字而不是格子边角。</summary>
    private const double BadgeOffsetRatio = 0.62;

    /// <summary>今天那一格的农历行改写这两个字 —— 用文字而不是装饰标记「今天」。</summary>
    private const string TodayCellLabel = "今天";

    /// <summary>今天的数字放大这么多倍。行高恒按普通字号算，所以放大不会顶动农历行。</summary>
    private const double TodayNumberScale = 1.22;

    /// <summary>
    /// 背景不透明度低于此值时，面板已经兜不住文字，自动把文字阴影加回来。
    /// 用户可以把「背景透明度」一路拉到 0，那时文字是直接浮在壁纸上的。
    /// </summary>
    private const double ShadowFallbackThreshold = 0.18;
    private const int MonthRecordPreviewCount = 30;
    private const int RecentRecordPreviewCount = 12;
    private const int TodayTodoPreviewCount = 6;

    private static readonly SolidColorBrush TodoMarkerBrush = CreateFrozenBrush(255, 209, 102);
    private static readonly SolidColorBrush DiaryMarkerBrush = CreateFrozenBrush(123, 223, 242);
    private static readonly SolidColorBrush ImportantBadgeBrush = CreateFrozenBrush(239, 71, 111);
    private static readonly SolidColorBrush TodoBadgeBrush = CreateFrozenBrush(46, 196, 182);
    private static readonly SolidColorBrush ListItemBrush = CreateFrozenBrush(18, 255, 255, 255);
    private static readonly SolidColorBrush ListItemBorderBrush = CreateFrozenBrush(36, 255, 255, 255);
    private static readonly SolidColorBrush NoteBorderBrush = CreateFrozenBrush(30, 255, 255, 255);
    private static readonly SolidColorBrush DeleteButtonBrush = CreateFrozenBrush(24, 239, 71, 111);
    private static readonly SolidColorBrush DeleteButtonBorderBrush = CreateFrozenBrush(60, 239, 71, 111);
    private static readonly SolidColorBrush ActionButtonBrush = CreateFrozenBrush(24, 255, 255, 255);
    private static readonly SolidColorBrush ActionButtonBorderBrush = CreateFrozenBrush(40, 255, 255, 255);
    /// <summary>分段控件里"选中"那一格的滑块。未选中项没有底色，胶囊底由外层统一提供。</summary>
    private static readonly SolidColorBrush ModeSelectedBrush = CreateFrozenBrush(56, 255, 255, 255);
    // ── 通道一：日期数字的颜色 = 法定属性 ──
    // 存成十六进制串而不是冻结画刷，好让 GetBrush 把透明度也算进缓存键 ——
    // 非本月的放假/调休需要同时降透明度。
    //
    // 取值不是写死的：文字色与其中一支撞色相时（预设「柔和青」撞休、「暖金」撞班），
    // HolidayPalette 会把那一支换到备用色。ApplySettings 是唯一的赋值点。
    private string _holidayOffColor = HolidayPalette.BaseOff;
    private string _holidayWorkColor = HolidayPalette.BaseWork;

    // ── 通道二：圆点与徽章 = 用户内容（重要用玫红，与放假的青绿拉开）──
    private static readonly SolidColorBrush ImportantMarkerBrush = CreateFrozenBrush(0xFF, 0x6B, 0x8A);

    // 徽章底色是亮色，文字必须用深色
    private static readonly SolidColorBrush BadgeTextBrush = CreateFrozenBrush(0x0B, 0x0E, 0x12);

    // ── 「今天」不在日期格里做记号 ──
    // 格子里曾经铺过 #21FFFFFF 填充 + #4DFFFFFF 内描边，与 hover 层（#20FFFFFF + 同色描边）
    // 撞得分不清"今天"和"鼠标停在这格"。前后十种形态（圆、环、短横、描边格、反白、字号跃升、
    // 下划线、游标、压暗、括号）都被否掉，结论是格子根本没地方容纳它 ——
    // 识别任务已经交给月历上方常驻的今日块，格子里只留数字加粗这一声。

    /// <summary>「5+2」分割线：上下渐隐的极细竖线。</summary>
    private static readonly LinearGradientBrush WeekendDividerBrush = CreateWeekendDividerBrush();

    // 画刷与阴影按 (颜色, alpha) 缓存并冻结：一次月历渲染原本会产生上百个可变的
    // SolidColorBrush 与 DropShadowEffect，全部参与 WPF 的变更通知与渲染管线。
    private readonly Dictionary<(string Color, byte Alpha), SolidColorBrush> _brushCache = [];
    private readonly Dictionary<byte, DropShadowEffect> _shadowCache = [];

    private readonly StorageService _storage = new();
    private readonly Dictionary<string, CalendarEntry> _entries;
    private readonly NoteListenerService _noteListener;
    private readonly HolidayService _holidays;
    private Forms.NotifyIcon? _trayIcon;
    private AppSettings _settings;
    private DateTime _visibleMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _lastTodayDate = DateTime.Today;
    private bool _isExitRequested;
    private ViewMode _mode = ViewMode.Calendar;
    private ViewMode? _modeBeforeSearch;
    private string _searchText = string.Empty;
    private DispatcherTimer? _searchDebounceTimer;
    private WpfButton[]? _dayButtons;
    private readonly List<WpfRectangle> _weekendDividers = [];
    private bool _showAllTodos;

    /// <summary>
    /// 当前挂在哪个桌面层宿主下面（<see cref="IntPtr.Zero"/> = 没挂）。
    /// 既用来判断要不要先摘回来，也用来把屏幕坐标换算成宿主客户区坐标。
    /// </summary>
    private IntPtr _desktopHost;

    /// <summary>桌面图标窗口（宿主下的 <c>SHELLDLL_DefView</c>）。桌面层要把自己钉在它之下。</summary>
    private IntPtr _desktopIconView;

    /// <summary>桌面层里最后一个合法位置（设备像素，相对宿主客户区），用于挡掉 WPF 的坐标漂移。</summary>
    private int _desktopX;
    private int _desktopY;

    /// <summary>位置自愈只记前几条，够排查又不会把日志刷满。</summary>
    private const int DesktopMoveLogLimit = 8;
    private int _desktopMoveLogCount;

    /// <summary>自愈时自己调 SetWindowPos，要防止由此触发的回调再次进来。</summary>
    private bool _isFixingDesktopPosition;

    /// <summary>桌面层的位置看守。只在嵌入桌面时运行，每秒一次两个 GetWindowRect，代价可忽略。</summary>
    private DispatcherTimer? _desktopWatchdog;
    private List<WebNoteGroup> _notes = [];
    private string? _editingNoteId;

    private enum ViewMode
    {
        Calendar,
        List,
        Note
    }

    public MainWindow()
    {
        InitializeComponent();

        _settings = _storage.LoadSettings();
        _entries = _storage.LoadEntries();
        _notes = LoadNotesWithIds();

        _holidays = new HolidayService(System.IO.Path.Combine(_storage.AppDataDirectory, "holidays"));
        // 数据可能来自后台线程的网络请求，回调里必须切回 UI 线程再重绘。
        _holidays.YearLoaded += _ => Dispatcher.BeginInvoke(RenderCalendar);

        InitializeTrayIcon();
        CreateStartupBackup();
        ApplySettings();
        RenderCalendar();
        StartDailyRefreshTimer();

        _noteListener = new NoteListenerService(_storage);
        _noteListener.OnNoteReceived += HandleNoteReceived;
        _noteListener.Start();

        // 第二个实例通过命名事件唤醒本实例（信号来自后台线程，需切回 UI 线程）。
        SingleInstanceService.StartShowListener(() => Dispatcher.Invoke(ShowWindowFromTray));

        SourceInitialized += (_, _) =>
        {
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
            HideMainWindowFromFastSwitcher();
            ApplyWindowLayer();
        };
        Loaded += (_, _) =>
        {
            ApplyStartupVisibility();
            WarnAboutRecoveredFiles();
        };
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Closing += MainWindow_Closing;
        Closed += (_, _) =>
        {
            _noteListener.OnNoteReceived -= HandleNoteReceived;
            _noteListener.Dispose();
            DisposeTrayIcon();
        };
    }

    private void WarnAboutRecoveredFiles()
    {
        var recovered = _storage.RecoveredFiles;
        if (recovered.Count == 0)
        {
            return;
        }

        WpfMessageBox.Show(
            this,
            $"以下数据文件无法解析，已被重命名隔离：\n\n{string.Join("\n", recovered)}\n\n" +
            $"应用已用空数据启动。可到备份目录找回：\n{_storage.BackupDirectory}",
            "透明日历",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void StartDailyRefreshTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        timer.Tick += (_, _) =>
        {
            if (DateTime.Today != _lastTodayDate)
            {
                _lastTodayDate = DateTime.Today;
                UpdateTrayIcon();
                RenderCalendar();
            }
        };
        timer.Start();
    }

    private void ApplySettings()
    {
        _brushCache.Clear();
        _shadowCache.Clear();

        // 假日两支颜色要按当前文字色避让，且必须在任何 Render* 之前算好 ——
        // 主题预设与自定义文字色都汇聚在这里，别处不要再推导一遍。
        (_holidayOffColor, _holidayWorkColor) = HolidayPalette.Resolve(_settings.TextColor);

        NormalizeWindowBounds();
        Left = _settings.Left;
        Top = _settings.Top;
        Width = _settings.Width;
        Height = _settings.Height;
        ResizeMode = _settings.IsLocked ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;

        var brush = TextBrush(_settings.TextOpacity);
        Root.Opacity = 1;
        MonthTitle.Foreground = brush;
        MonthTitle.FontSize = _settings.FontSize * FontScale.MonthTitle;
        MonthTitle.Effect = OptionalTextShadow(_settings.TextOpacity);
        // 今日块的字号/画刷全部由 RenderTodayPanel() 负责，这里不重复设置。

        // 只给 AppSurface 上色 —— 整个界面是一块表面，不要再给内部容器加底色，
        // 否则透明度会层层叠加，用户调的 35% 会变成实际 40%+。
        var bgAlpha = (byte)Math.Clamp(_settings.BackgroundOpacity * 255, 0, 255);
        var (panelR, panelG, panelB) = ThemePresets.PanelColorFor(_settings.ThemePreset);
        AppSurface.Background = FrozenBrush(WpfColor.FromArgb(bgAlpha, panelR, panelG, panelB));

        // 边框跟着背景一起淡出：背景全透明时还留一圈白框会很怪
        var borderAlpha = (byte)Math.Clamp(_settings.BackgroundOpacity * 170, 0, 90);
        AppSurface.BorderBrush = FrozenBrush(WpfColor.FromArgb(borderAlpha, 255, 255, 255));

        // 顶栏的文字与字形都跟着用户选的文字色走（放大镜是矢量描边，绑的也是 Foreground）
        if (SettingsBtn is not null)
        {
            TodayBtn.Foreground = brush;
            PrevMonthBtn.Foreground = brush;
            NextMonthBtn.Foreground = brush;
            SettingsBtn.Foreground = brush;
            CloseBtn.Foreground = brush;
            SearchToggleBtn.Foreground = brush;
            SearchCloseBtn.Foreground = brush;
        }

        UpdateModeButtons();

        // 窄窗口启动时 SizeChanged 未必来得比这里早，这里先按当前宽度定一次档。
        ApplyHeaderDensity();
        UpdateModeButtonLabels();
        RenderWeekHeader();
    }

    private void NormalizeWindowBounds()
    {
        var minLeft = SystemParameters.VirtualScreenLeft;
        var minTop = SystemParameters.VirtualScreenTop;
        var maxLeft = minLeft + SystemParameters.VirtualScreenWidth - 120;
        var maxTop = minTop + SystemParameters.VirtualScreenHeight - 120;

        _settings.Width = Math.Clamp(_settings.Width, MinWidth, SystemParameters.VirtualScreenWidth);
        _settings.Height = Math.Clamp(_settings.Height, MinHeight, SystemParameters.VirtualScreenHeight);
        _settings.Left = Math.Clamp(_settings.Left, minLeft, maxLeft);
        _settings.Top = Math.Clamp(_settings.Top, minTop, maxTop);
    }


    private void CalendarMode_Click(object sender, RoutedEventArgs e)
    {
        _modeBeforeSearch = null;
        SetMode(ViewMode.Calendar);
    }

    private void ListMode_Click(object sender, RoutedEventArgs e)
    {
        _modeBeforeSearch = null;
        SetMode(ViewMode.List);
    }

    private void NoteMode_Click(object sender, RoutedEventArgs e)
    {
        _modeBeforeSearch = null;
        SetMode(ViewMode.Note);
    }

    private void SetMode(ViewMode mode)
    {
        _mode = mode;
        CalendarViewPanel.Visibility = mode == ViewMode.Calendar ? Visibility.Visible : Visibility.Collapsed;
        ListViewPanel.Visibility = mode == ViewMode.List ? Visibility.Visible : Visibility.Collapsed;
        WebNoteViewPanel.Visibility = mode == ViewMode.Note ? Visibility.Visible : Visibility.Collapsed;
        UpdateModeButtons();
        RefreshCurrentView();
    }

    private void RefreshCurrentView()
    {
        switch (_mode)
        {
            case ViewMode.List:
                RenderListView();
                break;
            case ViewMode.Note:
                RenderWebNotes();
                break;
            default:
                RenderCalendar();
                break;
        }
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // WPF 的 TextBox 没有占位文字，只能自己盖一层，跟着内容显隐。
        SearchPlaceholder.Visibility = SearchTextBox.Text.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        // 每敲一个字都做全表扫描 + 重建 UI 太重，做一次短防抖。
        _searchDebounceTimer ??= CreateSearchDebounceTimer();
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private DispatcherTimer CreateSearchDebounceTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SearchDebounceMs) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            ApplySearch();
        };
        return timer;
    }

    private void ApplySearch()
    {
        _searchText = SearchTextBox?.Text.Trim() ?? string.Empty;

        if (_searchText.Length > 0)
        {
            // 只有从月历开始搜索才自动切到列表；已经在列表/笔记里就地过滤。
            if (_mode == ViewMode.Calendar)
            {
                _modeBeforeSearch = ViewMode.Calendar;
                SetMode(ViewMode.List);
                return;
            }

            RefreshCurrentView();
            return;
        }

        if (_modeBeforeSearch is { } previousMode)
        {
            _modeBeforeSearch = null;
            SetMode(previousMode);
            return;
        }

        RefreshCurrentView();
    }

    /// <summary>顶栏的拥挤程度。三档，越靠后砍得越多。</summary>
    private enum HeaderDensity
    {
        /// <summary>「2026年8月」+「月历 待做 笔记」。</summary>
        Full,

        /// <summary>标题只留「8月」。</summary>
        CompactTitle,

        /// <summary>模式按钮再收成单字。</summary>
        CompactAll
    }

    private HeaderDensity _headerDensity = HeaderDensity.Full;

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
        {
            ApplyHeaderDensity();
        }
    }

    private void ApplyHeaderDensity()
    {
        // 首次布局之前 ActualWidth 还是 0，那时按设置里的宽度算，否则会误判成最窄档。
        var width = ActualWidth > 0 ? ActualWidth : _settings.Width;

        var density = width < CompactAllWidth
            ? HeaderDensity.CompactAll
            : width < CompactTitleWidth
                ? HeaderDensity.CompactTitle
                : HeaderDensity.Full;

        // 每次 SizeChanged 都改 Text 会白白触发一轮布局，只在真正跨档时动。
        if (density == _headerDensity)
        {
            return;
        }

        _headerDensity = density;
        UpdateMonthTitle();
        UpdateModeButtonLabels();
    }

    /// <summary>
    /// 月份标题。中文排版不加空格：「2026 年 8 月」在 21px Light 下会散得很开。
    /// 窄窗口下只留「8月」—— 年份在翻月时基本不变，是这一行里冗余度最高的信息。
    /// </summary>
    private void UpdateMonthTitle()
    {
        MonthTitle.Text = _visibleMonth.ToString(
            _headerDensity == HeaderDensity.Full ? "yyyy年M月" : "M月",
            CultureInfo.GetCultureInfo("zh-CN"));
    }

    /// <summary>
    /// 最窄那一档把模式按钮收成单字。
    /// 没有换成图标：月历 / 待做 / 笔记都是抽象概念，小尺寸下的图标反而要猜，
    /// 而单字仍然直接可读；全称保留在 ToolTip 里。
    /// </summary>
    private void UpdateModeButtonLabels()
    {
        var compact = _headerDensity == HeaderDensity.CompactAll;
        CalendarModeButton.Content = compact ? "历" : "月历";
        ListModeButton.Content = compact ? "做" : "待做";
        NoteModeButton.Content = compact ? "记" : "笔记";
    }

    private void UpdateModeButtons()
    {
        SetModeButtonSelected(CalendarModeButton, _mode == ViewMode.Calendar);
        SetModeButtonSelected(ListModeButton, _mode == ViewMode.List);
        SetModeButtonSelected(NoteModeButton, _mode == ViewMode.Note);

        // 月份导航只在月历模式下有意义：待做与笔记跟"看哪个月"无关，
        // 留着它们只是让顶栏更挤。
        MonthNavGroup.Visibility = _mode == ViewMode.Calendar ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 分段控件的选中态：只靠一块实心滑块 + 字重，未选中项完全无底 ——
    /// 胶囊底由外层 <c>ModeSegment</c> 统一提供，每个按钮不再各自带底色与描边。
    /// </summary>
    private static void SetModeButtonSelected(WpfButton button, bool isSelected)
    {
        button.Opacity = isSelected ? 1 : 0.62;
        button.FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal;
        button.Background = isSelected ? ModeSelectedBrush : WpfBrushes.Transparent;
    }

    /// <summary>
    /// 展开 / 收起搜索条。它平时是收起的（十次里有九次用不到），展开时整条盖住顶栏。
    /// 收起时会清空搜索词，好让 <see cref="ApplySearch"/> 把视图退回搜索前的模式。
    /// </summary>
    private void ToggleSearchBar(bool show)
    {
        SearchOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        HeaderContent.Visibility = show ? Visibility.Collapsed : Visibility.Visible;

        if (show)
        {
            SearchTextBox.Focus();
            SearchTextBox.SelectAll();
            return;
        }

        if (SearchTextBox.Text.Length > 0)
        {
            SearchTextBox.Text = string.Empty;
        }

        Keyboard.ClearFocus();
    }

    private void SearchToggle_Click(object sender, RoutedEventArgs e)
    {
        ToggleSearchBar(SearchOverlay.Visibility != Visibility.Visible);
    }

    private double ScaledFont(double ratio, double minimum = 12)
    {
        return Math.Max(minimum, _settings.FontSize * ratio);
    }

    private SolidColorBrush TextBrush(double opacity)
    {
        return GetBrush(_settings.TextColor, opacity);
    }

    private SolidColorBrush GetBrush(string color, double opacity)
    {
        var alpha = (byte)Math.Clamp(opacity * 255, 0, 255);
        var key = (color, alpha);
        if (_brushCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        WpfColor parsed;
        try
        {
            parsed = (WpfColor)WpfColorConverter.ConvertFromString(color);
        }
        catch (Exception ex)
        {
            Log.Warn($"无法解析文字颜色 \"{color}\"，回退为白色。", ex);
            parsed = WpfColor.FromRgb(255, 255, 255);
        }

        parsed.A = alpha;
        var brush = FrozenBrush(parsed);
        _brushCache[key] = brush;
        return brush;
    }

    /// <summary>
    /// 有底板兜底时不加阴影 —— 每个 TextBlock 挂位图特效既脏又贵。
    /// 只有背景几乎全透明（文字直接浮在壁纸上）时才把阴影加回来。
    /// </summary>
    private DropShadowEffect? OptionalTextShadow(double opacity)
    {
        return _settings.BackgroundOpacity < ShadowFallbackThreshold ? TextShadow(opacity) : null;
    }

    private DropShadowEffect TextShadow(double opacity)
    {
        var strength = ThemePresets.ShadowStrengthFor(_settings.ThemePreset);
        var value = Math.Clamp(opacity * 0.65 * strength, 0.2, 0.95);
        var key = (byte)Math.Clamp(value * 255, 0, 255);
        if (_shadowCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var effect = new DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = strength > 1 ? 6 : 5,
            ShadowDepth = 0,
            Opacity = value
        };
        effect.Freeze();
        _shadowCache[key] = effect;
        return effect;
    }

    private static LinearGradientBrush CreateWeekendDividerBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new WpfPoint(0, 0),
            EndPoint = new WpfPoint(0, 1),
            GradientStops =
            {
                // 0x33 在亮壁纸上会变成一条穿过整个月历的亮线，比它要表达的"周末"还显眼。
                // 降到 0x1C 并把渐变收进来一点：需要时看得见，不需要时不抢戏。
                new GradientStop(WpfColor.FromArgb(0x00, 255, 255, 255), 0.0),
                new GradientStop(WpfColor.FromArgb(0x1C, 255, 255, 255), 0.18),
                new GradientStop(WpfColor.FromArgb(0x1C, 255, 255, 255), 0.82),
                new GradientStop(WpfColor.FromArgb(0x00, 255, 255, 255), 1.0)
            }
        };
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush FrozenBrush(WpfColor color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        return FrozenBrush(WpfColor.FromRgb(r, g, b));
    }

    private static SolidColorBrush CreateFrozenBrush(byte a, byte r, byte g, byte b)
    {
        return FrozenBrush(WpfColor.FromArgb(a, r, g, b));
    }
}
