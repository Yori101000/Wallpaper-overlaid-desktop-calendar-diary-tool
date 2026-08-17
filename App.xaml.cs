using System.IO;
using System.Windows;
using TransparentCalendar.Native;
using TransparentCalendar.Services;

namespace TransparentCalendar;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        Log.Initialize(Path.Combine(StorageService.DefaultAppDataDirectory, "logs"));

        // 诊断开关：跑一遍"能不能沉到桌面图标之下"的探针，把结论写进日志后退出。
        // 走在单实例检查之前 —— 探针不建主窗口、不读写数据文件，和正在跑的实例互不干扰。
        if (e.Args.Contains("--probe-desktop-layer", StringComparer.OrdinalIgnoreCase))
        {
            base.OnStartup(e);
            DesktopLayerProbe.Run();
            Shutdown();
            return;
        }

        // 已有实例在跑：唤醒它并静默退出，避免两个进程互相覆盖数据文件。
        if (!SingleInstanceService.TryAcquire())
        {
            Log.Info("检测到已有实例，唤醒后退出。");
            SingleInstanceService.NotifyExistingInstance();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("未处理的 UI 线程异常。", args.Exception);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Error("未处理的异常。", args.ExceptionObject as Exception);
        };

        base.OnStartup(e);

        Log.Info("应用启动。");

        // 主窗口在这里手工创建（而非 StartupUri），以便上面的提前退出真正生效。
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info("应用退出。");
        SingleInstanceService.Release();
        base.OnExit(e);
    }
}
