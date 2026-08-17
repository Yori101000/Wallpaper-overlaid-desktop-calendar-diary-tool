using System.Threading;

namespace TransparentCalendar.Native;

/// <summary>
/// 单实例保护。两个进程各自持有一份内存数据并全量回写同一批 JSON 文件时，
/// 后退出的那个会覆盖先退出的，因此必须限制为单实例。
///
/// 唤醒既有实例走的是**命名事件**而非窗口消息：早先的 HWND_BROADCAST 方案会向系统
/// 所有顶层窗口广播，只要有一个窗口无响应，第二次启动就会卡满超时时间。
/// </summary>
public static class SingleInstanceService
{
    private const string MutexName = @"Local\TransparentCalendar.SingleInstance";
    private const string ShowEventName = @"Local\TransparentCalendar.Show";

    private static Mutex? _mutex;
    private static EventWaitHandle? _showEvent;
    private static CancellationTokenSource? _listenerCancellation;

    /// <summary>尝试成为唯一实例。返回 false 表示已有实例在运行。</summary>
    public static bool TryAcquire()
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            if (!createdNew)
            {
                _mutex.Dispose();
                _mutex = null;
            }

            return createdNew;
        }
        catch
        {
            // 拿不到互斥体时宁可放行，也不要让用户完全打不开应用。
            return true;
        }
    }

    /// <summary>由唯一实例调用：后台等待唤醒信号，收到后回调（回调需自行切回 UI 线程）。</summary>
    public static void StartShowListener(Action onShowRequested)
    {
        try
        {
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        }
        catch
        {
            return;
        }

        _listenerCancellation = new CancellationTokenSource();
        var token = _listenerCancellation.Token;
        var handle = _showEvent;

        var thread = new Thread(() =>
        {
            var waitHandles = new WaitHandle[] { handle, token.WaitHandle };
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (WaitHandle.WaitAny(waitHandles) == 0)
                    {
                        onShowRequested();
                    }
                }
                catch
                {
                    return;
                }
            }
        })
        {
            IsBackground = true,
            Name = "TransparentCalendar.ShowListener"
        };

        thread.Start();
    }

    /// <summary>由第二个实例调用：定向唤醒既有实例，不做任何广播。</summary>
    public static void NotifyExistingInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out var handle))
            {
                using (handle)
                {
                    handle.Set();
                }
            }
        }
        catch
        {
            // 既有实例可能正在退出，唤醒失败不影响本进程退出。
        }
    }

    public static void Release()
    {
        _listenerCancellation?.Cancel();
        _listenerCancellation?.Dispose();
        _listenerCancellation = null;

        _showEvent?.Dispose();
        _showEvent = null;

        if (_mutex is null)
        {
            return;
        }

        try
        {
            _mutex.ReleaseMutex();
        }
        catch
        {
            // 未持有所有权时忽略。
        }

        _mutex.Dispose();
        _mutex = null;
    }
}
