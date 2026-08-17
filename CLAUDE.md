# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 项目概况

透明日历 (TransparentCalendar) —— 一个 WPF 桌面日历 / 待办 / 日记浮层，用于覆盖在已有的 Wallpaper Engine 壁纸之上。.NET 9 (`net9.0-windows`)，WPF + WinForms 互操作（托盘图标），单工程扁平结构，没有测试工程。

界面文案、持久化字符串、提交信息全部为中文。提交风格：`fix:` / `feat:` / `perf:` 前缀 + 中文摘要。

## 常用命令

```powershell
dotnet build .\透明日历.csproj
dotnet test  .\Tests\TransparentCalendar.Tests.csproj   # 191 个用例，必须全绿
dotnet run   --project .\透明日历.csproj   # 等价于 F5 运行
.\run.ps1                                   # 构建并运行，自动定位唯一的 .csproj
.\启动透明日历.bat                          # 同上，用于 PowerShell 脚本被策略禁用时
```

**不要在根目录放 .sln**：根目录已有一个 .csproj，再加 .sln 会让 `dotnet build` 报 MSB1011（无法判断该构建哪个）。测试工程要显式指定路径。
主工程用 `<Compile Remove="Tests/**" />` 把测试目录排除在默认通配符之外，新增测试目录时记得比照处理。

重新生成应用图标（`Assets\app.ico`，由 `Native/AppIcon.cs` 程序化绘制）：

```powershell
$env:TC_WRITE_ICON=1; dotnet test .\Tests\TransparentCalendar.Tests.csproj --filter "FullyQualifiedName~IconGeneratorTests"
```

工程文件名是中文，程序集名是 `TransparentCalendar`，产物在 `bin\Debug\net9.0-windows\TransparentCalendar.exe`。`run.ps1` / `run.bat` 特意用通配符查找 `*.csproj`，发现多于一个就报错退出 —— 若要在仓库根目录新增第二个工程，必须同时改这两个脚本。

没有测试、没有 lint、没有 CI。验证方式只有一种：把应用跑起来看。

浏览器扩展只有未打包的源码，通过 `chrome://extensions` → 加载已解压的扩展程序 指向 `BrowserExtension\` 即可，没有构建步骤。

## 架构

### `MainWindow` 拆成了 5 个 partial 文件

| 文件 | 职责 |
|---|---|
| `MainWindow.xaml.cs` | 字段、构造、`ApplySettings`、模式切换与搜索、顶栏窄窗口降级、画刷/阴影缓存 |
| `MainWindow.Calendar.cs` | 周头、42 个日期格、今日块、日期编辑器、农历/假日角标 |
| `MainWindow.List.cs` | 「待做」列表：单次遍历分桶 + 逾期/今天/未来分组渲染 |
| `MainWindow.Notes.cs` | 网页笔记：接收推送、卡片渲染、编辑器 CRUD |
| `MainWindow.Shell.cs` | 消息钩子、窗口层级、托盘、拖动、快捷键、设置往返、Win32 |

**无 UI 依赖的纯逻辑一律放 `Models/`**，不要塞回 `MainWindow`：`CalendarQuery`（查询/匹配/摘要/分组/提示/今日块文案）、`DateKeys`（`yyyy-MM-dd` 键）、`LunarCalendar`、`ThemePresets`、`HolidayPalette`（假日色避让）、`FontScale`。它们通过 `using static` 引入，所以界面代码里的调用写法和以前一样。这条约束是为了让 `Tests/` 能覆盖到 —— 一旦挪回 `MainWindow` 的私有成员，测试就够不着了。

### 界面构建仍然是命令式的

它同时持有设置、全部日历渲染、托盘图标、HTTP 笔记监听服务和 Win32 P/Invoke。没有 MVVM、没有 ViewModel，几乎不用数据绑定：`RenderCalendar()` 清空 `CalendarGrid`，然后以命令式方式重建全部 42 个日期按钮（`CreateDayButton` → `CreateDayContent`）。唯一例外是 `DayEditorWindow`，它绑定了 `ObservableCollection<TodoItem>`。

由此带来的后果：改动视觉效果通常要动**两处** —— `ApplySettings()`（从 `AppSettings` 推导颜色、透明度、字号）和对应的 `Render*` 方法。`MainWindow.xaml` 里写死的 `Background="#50181818"` 之类只是启动时的兜底值，`ApplySettings()` 会在运行时按 `BackgroundOpacity` 覆盖 `AppSurface` 的背景（整个界面只有这一块表面上色）。

### 三种视图模式共用一块内容区

`CalendarViewPanel`、`ListViewPanel`、`WebNoteViewPanel` 靠 `Visibility` 切换，由单一字段 `_mode`（`ViewMode.Calendar/List/Note`）跟踪 —— 统一走 `SetMode()`，它负责 Visibility、`UpdateModeButtons()` 和 `RefreshCurrentView()`。

搜索是"在当前模式内过滤"：只有从月历开始搜索才自动切到列表（并把原模式记进 `_modeBeforeSearch`，清空时回去）；已经在列表/笔记里就地过滤。用户手动点模式按钮会清掉 `_modeBeforeSearch`。搜索输入有 200ms 防抖。

搜索条**平时是收起的**（`SearchOverlay`，`Visibility=Collapsed`）：点顶栏放大镜或 `Ctrl+F` 展开并盖住顶栏内容，`Esc` 或 ✕ 收起。收起时会清空搜索词，正好触发上面那条"清空后退回原模式"的逻辑。**不要把它改回常驻一行** —— 十次里有九次用不到，却要占 38px。

### 顶栏承载全部"操作外壳"

月份导航（`MonthNavGroup`）、模式分段（`ModeSegment`）、搜索、设置、关闭全在 `HeaderBar` 一行里。原先它们散在三处：顶栏一行、左侧栏一列 68px、搜索一行 38px，而侧栏三个按钮只占顶部 186px，底下约 560px 是空的。**左侧栏已删除**，连带 `SidebarPosition` 设置项一起。

顶栏的控件语言只有两种，别加第三种：

- **`GhostButtonStyle`** —— 无底、无描边，hover / 键盘焦点时才浮现 `#1AFFFFFF`。界面是**单层**半透明表面，按钮再各自带底色与白描边就是"盒子套盒子"的回潮（那正是重构前难看的根源）。
- **`SegmentButtonStyle`** —— 三个模式共享一个胶囊底，选中项是一块实心滑块（`ModeSelectedBrush`）。它表达"三选一"，与幽灵按钮的"点一下做件事"是两回事。

`‹ ›` 箭头贴着月份标题成一组，不是三个分离的方块；非月历模式下整组隐藏（待做与笔记跟"看哪个月"无关）。

**窄窗口降级**：窗口可拖到 480px，WPF 没有媒体查询，只能在 `MainWindow_SizeChanged` 里按 `ActualWidth` 分三档（`ApplyHeaderDensity`）——
- ≥560：`2026年8月` + `月历 待做 笔记`
- 500~560：标题只留 `8月`（年份翻月时基本不变，冗余度最高）
- <500：模式按钮收成单字 `历 做 记`（**不用图标**：三个都是抽象概念，小尺寸图标反而要猜；全称在 ToolTip 里）

只在跨档时才改文本，否则每次 `SizeChanged` 都白白触发一轮布局；首次布局前 `ActualWidth` 是 0，那时要退回 `_settings.Width` 判断，不然窄档会误判。

### 设置的往返流程

`SettingsWindow` 编辑的是 `AppSettings.Clone()` 出来的**副本**，通过 `Settings` 属性暴露；`MainWindow.OpenSettings()` 替换 `_settings`、持久化，然后依次调用 `ApplySettings()` + `RenderCalendar()` + `ApplyWindowLayer()`。新增一个设置项要改四处：`Models/AppSettings.cs`、`ApplySettingsToControls()`、`Save_Click()`，以及 `MainWindow.ApplySettings()` 里的消费点（拷贝已由 `Clone()` 自动覆盖）。

`StartOnBoot` 不像其他项存在 JSON 里，它通过 `StartupService` 落在注册表 `HKCU\...\CurrentVersion\Run`，并在设置窗口打开时反向读回到设置副本中。

`AppSettings.Normalize()` 在每次载入后调用，负责把历史字段迁移到当前 schema（例如旧的 `KeepOnTop`/`AttachToDesktopLayer` → `WindowLayer`）并夹住非法取值。旧字段声明为 `bool?` + `JsonIgnore(WhenWritingNull)`，迁移后自动从文件里消失。**破坏性的模型变更会让 JSON 解析失败进而触发隔离，请始终走 Normalize 做迁移，不要直接改字段语义。**

### 窗口层级 —— 默认路线不碰 WorkerW

默认方案：`AllowsTransparency="True"` + `WindowStyle="None"` 的普通分层窗口，`WindowLayer` 四态 ——
- `Top` → `Topmost = true`
- `Normal` → 原样
- `Bottom` → 在 `MainWindow.WndProc` 里拦 `WM_WINDOWPOSCHANGING` 调 `WindowLayerService.ForceBottom`，把 Z 序钉在最底。**单次 `SendToBottom` 不够**，任何一次激活都会把窗口重新抬起来。这样窗口在所有普通窗口之下、壁纸之上（但仍在桌面**图标之上**），且不影响键盘焦点（不能用 `WS_EX_NOACTIVATE`，主窗口有搜索框需要输入）。
- `Desktop`（「嵌入桌面」，默认关）→ `SetParent` 到 WorkerW，落到桌面**图标之下**。见下。

#### 嵌入桌面层（`Desktop`）

早年 `SetParent` 到 `WorkerW`/`Progman` 试过一次就放弃了，当时归因于 Wallpaper Engine。**这个归因是错的**，见下面第 2 条 —— 真正的原因是 Z 序压错了层，WE 没开也一样看不见。

`Native/DesktopLayerProbe` 是这条路的探针，跑 `TransparentCalendar.exe --probe-desktop-layer` 会把结论写进日志后退出（不建主窗口、不动数据文件）。Win11 实测挂载、半透明（`WS_EX_LAYERED`）、键盘焦点三项都能保住。

这条路上踩过的坑，一个都别再踩：

1. **`SetParent` / `GetParent` 都判不出成败。** `SetParent` 返回的是**原**父窗口，顶层窗口原本没有父窗口，成功时也返回 `NULL`；`GetParent` 对非子窗口返回的是 owner（这里恒为 0）。只有 **`GetAncestor(hwnd, GA_PARENT)`** 是真父窗口。
2. **不能压到 Z 序最底。** Win11 上 Progman 底下同时挂着 `SHELLDLL_DefView`（桌面图标）**和一个画壁纸的 WorkerW**，壁纸那个在图标之下。`HWND_BOTTOM` 会落到壁纸之下被整块盖住 —— 这就是当年"SetParent 之后完全看不见"的真相。正确位置是 `SetWindowPos(hwnd, defView, …)`，即**紧贴桌面图标之下**。而且只钉一次不够，要在 `WM_WINDOWPOSCHANGING` 里用 `ForceInsertAfter` 持续钉住（同 `Bottom` 模式的道理）。
3. **候选宿主要按"可见 + 面积"筛。** 实测枚举出 17 个没有 DefView 的 WorkerW，全是隐藏的零尺寸僵尸窗口，挂上去能成功但根本不显示。另外若没有任何 WorkerW 带 DefView，说明图标挂在 Progman 下，那 Progman 才是要进的那一层。催生 WorkerW 的 `0x052C` 必须**定向发给 Progman**，不能 `HWND_BROADCAST`（会挨个等所有顶层窗口，一个无响应就卡满超时）。
4. **只支持虚拟桌面原点为 (0,0) 的布局**（`WindowLayerService.IsDesktopLayerSupported`）。挂进宿主后窗口位置对 OS 是相对宿主客户区的，而 WPF 记的是屏幕坐标，两者差一个宿主原点。副屏排在主屏左侧时（实测原点 -1920），WPF 会在**不发任何窗口位置消息**的情况下把位置改回它记的屏幕坐标，OS 又把那个值当相对坐标用 —— 每轮多偏 1920px，一次就甩出屏幕；`WM_WINDOWPOSCHANGING` 拦不到，事后自愈也只能每秒纠一次（会闪）。原点为 0 时差值为 0，天然不冲突。不支持的布局直接弹框说明并退回 `Normal`，**不要**尝试补偿。
5. **桌面层里绝不碰 WPF 的 `Left`/`Top`**，一律走 `MoveWithinHost`（`SetWindowPos`，宿主相对、设备像素）；落盘位置也要回读 `ScreenRect` 换算，别用 `Left`/`Top`。

其余约定：
- 切换层级时 `ApplyWindowLayer()` 先 `DetachFromDesktop` 再重挂，靠 `_desktopHost` 跟踪状态（顺带缓存 `_desktopIconView` 供钉 Z 序用）。
- 挂不上就把 `WindowLayer` 改回 `Normal` **并落盘**，避免每次启动都白试。
- WE 在跑时选这一项会先弹确认框（`ConfirmDesktopLayer`），并告知托盘里有「恢复为普通窗口」。**那个托盘菜单项是唯一的退路，不要删** —— 日历看不见时窗口本身点不到。
- 桌面层下不要 `Activate()`（`BringIntoViewOnce` 已跳过）：那时窗口是桌面的子窗口，激活只会把桌面提到前面。`ForceBottom` 只在 `Bottom` 下生效，两套 Z 序策略不能同时来。

另外 `HideMainWindowFromFastSwitcher()` 设置 `WS_EX_TOOLWINDOW`、清除 `WS_EX_APPWINDOW`，使浮层不出现在 Alt+Tab 里。每次 `Show()` 之后都要重新施加一次（见 `ShowWindowFromTray`）。

**副作用**：主窗口是 tool window 且 `ShowInTaskbar="False"`，所以 `Process.MainWindowHandle` 为 0，UI Automation 也不会把设置/日期编辑对话框列为桌面的直接子窗口 —— 它们挂在主窗口的 UIA 子树下。用脚本驱动 UI 时要从主窗口元素往下 `TreeScope.Descendants` 找，否则会误判成"对话框没打开"。

### 单实例

`App.OnStartup` 用命名 Mutex（`Native/SingleInstanceService`）保证单实例：已有实例时通过**命名事件**（`EventWaitHandle`）定向唤醒它，然后 `Shutdown()`。主窗口用 `StartShowListener` 起一个后台线程等待信号。

早先用的是 `SendMessageTimeout(HWND_BROADCAST, …)`：那会向系统所有顶层窗口广播，只要有一个窗口无响应，第二次启动就会卡满超时时间。**不要退回窗口消息方案。**

因此 `App.xaml` **没有** `StartupUri` —— 主窗口在 `OnStartup` 里手工创建，否则提前 `Shutdown()` 拦不住窗口创建。

### 持久化（`Services/StorageService.cs`）

全部状态位于 `%AppData%\透明日历\`：`settings.json`、`calendar-data.json`、`web-notes.json`、`backups\`。

- 日历条目是以 `yyyy-MM-dd` 为键的 `Dictionary<string, CalendarEntry>`（`DateKey` / `ParseDateKey`，始终用 `InvariantCulture`）。请用这两个辅助方法，别自己拼日期格式。
- **所有写入都走 `WriteAtomic`**（临时文件 + `File.Replace`），所有 `Load*`/`Save*` 都在 `_sync` 锁内 —— 笔记监听线程与 UI 线程会同时读写同一批文件。新增读写方法务必沿用这两条。
- 每次保存整文件重写 —— 当前数据量下没问题，但不要引入逐键盘输入即保存的逻辑。
- 落盘日历条目请走 `MainWindow.PersistEntries()` 而不是直接 `SaveEntries`：它会先用 `EntryHasContent` 剔除空条目。
- 无法解析的 JSON 会被改名为 `<文件>.broken-<时间戳>.bak`，文件名记入 `RecoveredFiles`，主窗口 `Loaded` 时提示用户。
- 启动备份按天去重（`CreateAutomaticBackup(..., force: false)`），导入前强制写一份（`force: true`）；`PruneBackups` 保留最新 20 份，**按文件名排序**（Windows 文件时间隧道会让创建时间不可靠）。备份文件名精度只到秒，同秒冲突时会自动追加序号 —— 否则导入前的保命备份会盖掉刚写的启动备份。
- `StorageService` 构造函数可注入数据目录，**仅供测试**传临时目录用；正式运行传 null 走 `DefaultAppDataDirectory`。测试绝不能碰真实 `%AppData%`。

### 网页笔记接收

`NoteListenerService` 在 `http://localhost:51999/` 上跑 `HttpListener`，接收 `POST /save`，请求体为 `{url, title, text}`。防护：拒绝 >64KB 的请求体（413）、校验 `Origin`（只放行浏览器来源）、回显具体 Origin 而非 `*`、URL 必须是 http/https。**注意局限**：bookmarklet 从用户当前页面发出，其 Origin 与恶意页面无法区分 —— 这一层挡不住恶意网页，要真正封死得上 token。

笔记按页面归组：URL 经 `WebUrl.TryNormalize` 归一化为 `scheme://host/path`，命中已有 `WebNoteGroup` 就把文本追加进 `Notes`，否则新建一组。读-改-写整体走 `StorageService.UpdateWebNotes`（锁内完成），**不要**用"UI 侧持有快照再整表覆盖"的写法。同理，笔记编辑/删除按 `WebNoteGroup.Id` 定位而非对象引用 —— 扩展随时可能推送新笔记并整体换掉 `_notes`。

`OnNoteReceived` 在监听线程上触发，`MainWindow` 通过 `Dispatcher.Invoke` 切回 UI 线程。

**端口不是固定的**：51999 被占用时 `Start` 会依次回退到 52008。桌面端在笔记页显示实际端口并据此生成 bookmarklet；扩展（`background.js` / `popup.js`）通过 OPTIONS 探测整个区间并把结果缓存进 `chrome.storage`，`manifest.json` 的 `host_permissions` 因此放宽为 `http://localhost/*`。

扩展（MV3）的触发入口只有**右键菜单**和 `Ctrl+Shift+S`（`chrome.commands`）。工具栏图标打开的是 popup（状态面板），不要再加 `chrome.action.onClicked` —— 声明了 `default_popup` 后它永远不会触发。也不要在 `content.js` 里监听 Ctrl+Shift+S，那只会白白吞掉所有网站上的这个组合键。

### 待办推迟

`DayEditorWindow.PostponeTodo_Click` **只在窗口内登记**到 `PendingPostpones`，不落盘；`MainWindow.OpenDayEditor` 在对话框返回 `true` 后才逐条 `AddTodoToDate` 并 `PersistEntries()`。点"取消"则整批丢弃 —— 这是为了让"取消"真的能撤销推迟。
`PostponedFromDate` 会在多次推迟之间一路传递下去，因此 `PostponedDays` 是相对最初日期累计的，而不是相对上一次。

### 字符串字面量带有语义

`TodoItem.Priority` 是按序数与 `"重要"` 比较的（`CalendarQuery.IsImportantTodo`），默认值 `"普通"` —— 它们是数据而非展示文案，改动会让已有的 `calendar-data.json` 失效。同理 `ThemePreset` 的名字是 `Models/ThemePresets.All` 的键（预设同时决定文字颜色、不透明度、阴影强度和面板底色），`WindowLayer` 存 `"Bottom"`/`"Normal"`/`"Top"`/`"Desktop"`，而下拉框显示的是中文。（`SidebarPosition` 已废弃：侧栏没了，字段只为兼容老 JSON 而保留，没有代码读它 —— 删字段会让反序列化失败并触发文件隔离。）

**`AppSettings` 里用于迁移判断的字段默认值必须留空**：`WindowLayer` 的默认值是 `string.Empty` 而不是 `"Normal"`，否则 `Normalize()` 会把"字段缺失"误判成"已是合法值"，老用户的设置就被静默丢弃了（这个 bug 是被单元测试抓出来的）。

### 视觉体系：各占一条通道，互不侵占

日期格里的信息各占一条**表达通道**。这是刻意设计的，别把它们混到一起：

| 信息 | 通道 | 实现 |
|---|---|---|
| 法定属性（放假/调休/非本月） | **数字颜色** | `_holidayOffColor` 青 / `_holidayWorkColor` 橙（按文字色避让，见下）/ `AdjacentMonthOpacity` 压暗 |
| 周末 | **结构**（不用颜色） | 「5+2」竖分割线，见下 |
| 今天 | **不在格子里做记号** | 月历上方常驻的**今日块**；格子里是数字放大一档（`TodayNumberScale` 1.22）+ `Bold` + 农历行改写「今天」 |
| 用户内容 | **圆点与徽章** | 日记 = 一枚 `DiaryMarkerBrush` 青点；待办 = 右上角 14px 徽章（`TodoMarkerBrush` / 重要 `ImportantMarkerBrush`） |
| 鼠标悬停 | **填充圆角矩形** | `MainWindow.xaml` 的 `HoverLayer`，`#20FFFFFF` + 同色描边，**独占这一族** |

颜色决策是纯函数 `CalendarQuery.ResolveEmphasis()`，优先级 **放假 > 调休 > 非本月 > 普通** 被单测钉死。
放假/调休**故意排在非本月之前**：跨月首尾行会显示邻月日期，而"邻月的国庆放假"依然要看得见。

早先左上角有个 14×14 的「休/班」方块，已删除 —— 信息由数字颜色 + 农历行末尾的 `HolidaySuffix()` 承载。
**不要再往格子里加装饰**：格子只有 ~45×52px，装饰互相争夺注意力是这次重做要解决的问题。

**「今天」不要再往格子里放记号（装饰意义上的）。** 格子里曾经铺 `#21FFFFFF` 填充 + `#4DFFFFFF` 内描边，与 hover 层撞得分不清"今天"和"鼠标停在这格"。前后试过并被否掉十种形态：实心圆、细环圆、底部短横、圆角描边格、反白块、紧下划线、顶部游标、其余日期压暗、尖括号包夹 —— 结论是格子没有空间容纳**装饰**。

最终采用的是三重**非装饰**信号，且都不占新空间：今日块（见下）+ 农历行改写「今天」+ 数字放大一档并加粗。
注意"字号跃升"当初也在被否之列 —— 那是它作为**孤立标记**时被否的；现在它只是第三重冗余，且 `LineHeight` 恒按普通字号算，放大不会顶动农历行、不破坏整行基线。**只靠字重是不够的**：一屏 42 个数字里实测找不到。

**待办不再有圆点。** 原先「有待办」同时给一枚 5px 琥珀圆点和一枚琥珀徽章，同色、同义，说了两遍。现在只留徽章（带数量，信息更多）。**副作用**：只有已完成待办的日子，格子里不再有任何痕迹 —— 信息仍在悬停提示与「待做」列表里。

### 今日块（`TodayPanel`）

`CalendarViewPanel` 最上面那块（原 `TodayTodoPanel`），排在**周头之前**。它**常驻显示**，不再"有未完成待办才出现"—— 时有时无的话，"今天"就没有稳定的落点。

**整块只占一行**：大号日号 │ 星期与农历 │ 待办摘要，三列同一条垂直中线，摘要装不下就裁剪。原先摘要另起一行，于是左侧挤成一团、右边三分之二全空，是整屏最失衡的一块。**不要让它再向下长**。

周头的对比度要**高于**格子里的农历行（`TextOpacity × 0.68`，周末 `× 0.86`）：它是读懂整张表的钥匙。原先压到 0.42/0.58，比农历行还淡，层级是反的。

内容由 `RenderTodayPanel()` 一次性刷：大号日号（`FontScale.TodayNumber`）、`BuildTodayDateLine()`、`BuildTodayLunarLine()`、假日 chip、`BuildTodayUnfinishedLabel()` + 待办摘要（无待办时是 `CalendarQuery.TodayEmptyHint` 一句轻提示）。文案全是 `Models/CalendarQuery.cs` 里的纯函数，有单测。

它读 `DateTime.Today` 而不是 `_visibleMonth`：**翻月份时内容不变**，这是有意的。
假日 chip 是唯一允许颜色参与「今天」表达的地方 —— 它承载的是法定属性（通道一），不是"今天"本身。

### 日期格是三行定高

`CreateDayContent()` 的 `Grid` 是 `* / 数字 / 农历 / 圆点 / *` 五行，中间三行**定高**，首尾两个 `*` 把这一块居中；徽章靠 `RowSpan=5` 覆盖整格才能落在真正的右上角。

定高是为了让数字的垂直位置与格子里有没有内容**无关**。原先是居中的 `StackPanel`，子元素数量随内容变化，于是同一行里有待办的格子数字被顶高、空格子偏低 —— 整行数字高低不齐，这是"糊"的主因。**不要改回 StackPanel。**

农历行是否占位按 `_settings.ShowLunar || _settings.ShowStatutoryHolidays` **统一决定**，不看这一格自己有没有内容：关掉农历后只有假日那几天带 `HolidaySuffix()`，若按格判断，基线又会参差。

### 假日两支颜色按文字色避让

`Models/HolidayPalette.cs`（纯逻辑、无 UI 依赖、有单测）。数字颜色这条通道同时被"用户选的文字色"和"法定属性"使用，撞色就等于失效：预设「柔和青」`#7BDFF2` 距基准休色只有 26°，「暖金」`#FFD166` 距基准班色只有 15°。

规则不是预设对照表，而是按文字色现算：每一支在自己的候选序列里挑**第一个**色相距离 ≥45° 的颜色；一个都不达标时**退而取最远的**。两条都必要 —— 顺序优先避免无谓换色（只撞休时班应保持基准橙），兜底取最远避免阈值坑（纯绿距基准休色 43°、距备用只有 28°，无脑换下一个反而更糟）。饱和度 < 0.15（白/浅灰/高对比）不触发。

班色有三支（橙 → 玫红 → 紫）：红系文字色距前两支都不够远。休只有两支，唯一缺口在 120° 附近，那时基准仍有 43°，够用。

`ApplySettings()` 是唯一的赋值点（清画刷缓存之后立刻算），别处不要再推导一遍。

### 「5+2」周末分割线

`RebuildWeekendDividers()` 往 `CalendarGrid` 加 `RowSpan=6` 的 1px `Rectangle`，画刷是冻结的垂直渐变（两端透明）。

**易漏的分支**：周末列是否相邻取决于一周从哪天开始 ——
- 周一起始 → `一二三四五 | 六日`，**1 条**线
- 周日起始 → `日 | 一二三四五 | 六`，周末分居两端，**必须 2 条**线

列号由 `CalendarQuery.WeekendDividerColumns()` 决定（有单测）。`EnsureDayButtons()` 与 `RenderWeekHeader()` 都会重建分割线，因为改「周一作为一周开始」时列位置会变。

### 文字投影是兜底，不是常态

界面已改为**单层半透明表面**（`CalendarViewPanel` / `ListViewPanel` / `WebNoteViewPanel` 只留 `Padding`，底色与边框统一由 `MainContentPanel` 承载）。原先三层嵌套的实际黑度 ≈40%，比用户在设置里调的数值更闷。

因此文字**默认不加投影** —— 一律走 `OptionalTextShadow()` 而不是 `TextShadow()`。
但 `BackgroundOpacity` 可以被拉到 0，那时文字直接浮在壁纸上，所以 `OptionalTextShadow` 在低于 `ShadowFallbackThreshold`（0.18）时会把阴影加回来。**新增文字元素请用 `OptionalTextShadow`**。

中文不要用 `FontWeight.Light`（雅黑 Light 在半透明底上会糊）：日期数字 Medium、周头 SemiBold，只有月份标题这种大字号保留 Light。窗口级 `FontFamily="Microsoft YaHei UI, Segoe UI"`，日期数字加了 `Typography.NumeralAlignment=Tabular` 让 7 列数字等宽。

### 渲染性能上的既定约束

- 画刷与阴影一律走 `TextBrush()`/`GetBrush()`/`TextShadow()` 的**冻结缓存**，静态画刷也都 `Freeze()` 过。不要在渲染循环里 `new SolidColorBrush` 或 `new DropShadowEffect` —— 一次月历渲染会产生上百个。`ApplySettings()` 负责清缓存。
- 42 个日期按钮由 `EnsureDayButtons()` 只构建一次，之后 `UpdateDayButton()` 只换 Content/Tag/ToolTip/Background。
- `RenderCalendar()` 只在 `_mode == List` 时才调 `RenderListView()`；`RenderListView` 对 `_entries` **单次遍历**分桶出三个分区，不要退回成每个分区各扫一遍。

### 农历与法定假日 —— 两套完全不同的机制

**农历 / 节气 / 传统节日**（`Models/LunarCalendar.cs`）：基于 .NET 内置的 `ChineseLunisolarCalendar`，**纯离线零依赖**。
- 除夕靠"次日是初一"判断，**不能**假设腊月固定 30 天。
- 有闰月的年份，`GetMonth` 返回的月序号在闰月之后要减一才对应实际月份。
- 24 节气用寿星公式，C 常数分 20 / 21 世纪两套，**混用会让多数节气整体偏一天**（曾经踩过）。2026 全年 24 个节气在测试里逐个钉死了，改动常数会立刻失败。

**法定假日与调休**（`Services/HolidayService.cs`）：**这是本应用唯一的对外网络请求。**
调休由国务院每年单独发文，没有算法可推，Windows 与 .NET 也都没有开放接口 —— 只能拉公开数据源。
- 双源：`timor.tech`（国内优先）→ `raw.githubusercontent.com/NateScarlet/holiday-cn`（兜底）。两源已交叉验证一致。
- 归一化为**自有 schema** 后缓存到 `%AppData%\透明日历\holidays\{year}.json`，换源不影响已有缓存。
- 当年数据缓存 30 天后重拉，往年数据永久有效。
- **绝不阻塞 UI**：`Find()` 只读内存，没有就返回 null；`EnsureYear()` 后台拉取，完成后经 `YearLoaded` 事件切回 UI 线程重绘。拉取失败静默降级（用缓存 → 无缓存则不显示角标）。
- 设置项 `ShowStatutoryHolidays` 关闭后**一个请求都不会发**。

### 日志

`Services/Log.cs` 写 `%AppData%\透明日历\logs\yyyyMMdd.log`，保留 7 天。
`Initialize()` 之前的写入是空操作，所以测试和启动早期调用都安全。
新增 catch 块时请顺手 `Log.Warn/Error` —— 十几处静默 catch 曾让一次排查多花了好几轮。

### 托盘与退出

`ShutdownMode="OnMainWindowClose"`。开启 `CloseToTray` 时，`MainWindow_Closing` 会取消关闭并隐藏窗口；真正的退出走 `ExitApplication()`，它会先置 `_isExitRequested`。窗口位置尺寸在关闭时、隐藏到托盘时、打开设置前都会保存一次。
