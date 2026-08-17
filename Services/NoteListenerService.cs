using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TransparentCalendar.Models;

namespace TransparentCalendar.Services;

public sealed class NoteListenerService : IDisposable
{
    public const int DefaultPort = 51999;
    private const int PortAttempts = 10;
    private const int MaxBodyBytes = 64 * 1024;

    private readonly StorageService _storage;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private HttpListener? _listener;
    private bool _running;
    private int _port;

    public int Port => _port;
    public bool IsRunning => _running;

    public event Action? OnNoteReceived;

    public NoteListenerService(StorageService storage)
    {
        _storage = storage;
    }

    /// <summary>
    /// 从 <paramref name="startPort"/> 起依次尝试若干端口。返回 false 表示全部被占用，
    /// 调用方应把这一情况显示给用户 —— 否则界面上的 bookmarklet 会指向一个没人监听的端口。
    /// </summary>
    public bool Start(int startPort = DefaultPort)
    {
        for (var offset = 0; offset < PortAttempts; offset++)
        {
            var port = startPort + offset;
            var listener = new HttpListener();
            try
            {
                listener.Prefixes.Add($"http://localhost:{port}/");
                listener.Start();
            }
            catch (Exception ex)
            {
                Log.Warn($"端口 {port} 无法监听，尝试下一个。", ex);
                listener.Close();
                continue;
            }

            _listener = listener;
            _port = port;
            _running = true;
            _ = ListenLoop();
            Log.Info($"网页笔记监听已启动，端口 {port}。");
            return true;
        }

        _running = false;
        Log.Error($"端口 {startPort}–{startPort + PortAttempts - 1} 全部被占用，网页笔记监听未启动。");
        return false;
    }

    public void Stop()
    {
        _running = false;
        var listener = _listener;
        _listener = null;
        if (listener is null)
        {
            return;
        }

        try
        {
            listener.Stop();
        }
        catch
        {
            // 已经停掉时忽略。
        }

        listener.Close();
    }

    private async Task ListenLoop()
    {
        var listener = _listener;
        while (_running && listener is not null)
        {
            try
            {
                var ctx = await listener.GetContextAsync();
                _ = HandleRequest(ctx);
            }
            catch
            {
                break;
            }
        }
    }

    private async Task HandleRequest(HttpListenerContext ctx)
    {
        try
        {
            var request = ctx.Request;
            var response = ctx.Response;

            var origin = request.Headers["Origin"];
            if (!IsAllowedOrigin(origin))
            {
                Respond(response, 403);
                return;
            }

            ApplyCorsHeaders(response, origin);

            if (request.HttpMethod == "OPTIONS")
            {
                response.Headers.Add("Access-Control-Allow-Methods", "POST, OPTIONS");
                response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
                Respond(response, 204);
                return;
            }

            if (request.HttpMethod != "POST")
            {
                Respond(response, 405);
                return;
            }

            if (request.Url?.AbsolutePath != "/save")
            {
                Respond(response, 404);
                return;
            }

            if (request.ContentLength64 <= 0 || request.ContentLength64 > MaxBodyBytes)
            {
                Respond(response, 413);
                return;
            }

            var body = await ReadBodyAsync(request);
            NoteRequest? data;
            try
            {
                data = JsonSerializer.Deserialize<NoteRequest>(body, _jsonOptions);
            }
            catch
            {
                data = null;
            }

            if (data is null || !WebUrl.TryNormalize(data.Url, out var url))
            {
                Respond(response, 400);
                return;
            }

            var title = string.IsNullOrWhiteSpace(data.Title) ? WebUrl.ExtractDomain(url) : data.Title.Trim();
            var text = data.Text?.Trim() ?? string.Empty;

            // 读-改-写整体在 StorageService 的锁内完成，避免与 UI 线程的保存互相覆盖。
            _storage.UpdateWebNotes(notes =>
            {
                var existing = notes.Find(n => n.Url == url);
                if (existing is not null)
                {
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        existing.Notes.Add(text);
                    }

                    existing.UpdatedAt = DateTime.Now;
                    return;
                }

                notes.Add(new WebNoteGroup
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = title,
                    Url = url,
                    Notes = string.IsNullOrWhiteSpace(text) ? [] : [text],
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                });
            });

            response.ContentType = "application/json";
            var ok = Encoding.UTF8.GetBytes("{\"ok\":true}");
            response.ContentLength64 = ok.Length;
            await response.OutputStream.WriteAsync(ok);
            response.Close();

            OnNoteReceived?.Invoke();
        }
        catch
        {
            try
            {
                ctx.Response.Abort();
            }
            catch
            {
                // 连接已断开。
            }
        }
    }

    /// <summary>
    /// 按声明的 Content-Length 读满为止，避免超长请求体拖垮进程。
    /// </summary>
    private static async Task<string> ReadBodyAsync(HttpListenerRequest request)
    {
        var buffer = new byte[request.ContentLength64];
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await request.InputStream.ReadAsync(buffer.AsMemory(offset));
            if (read <= 0)
            {
                break;
            }

            offset += read;
        }

        return Encoding.UTF8.GetString(buffer, 0, offset);
    }

    /// <summary>
    /// 只放行浏览器来源。注意：bookmarklet 是从用户当前页面发出的，它的 Origin
    /// 与任意网页无法区分 —— 这一层能挡住畸形/非浏览器来源的写入，挡不住恶意网页。
    /// </summary>
    private static bool IsAllowedOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin) || origin == "null")
        {
            // 无 Origin：本机脚本或扩展的后台请求，放行。
            return true;
        }

        return origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase)
            || origin.StartsWith("moz-extension://", StringComparison.OrdinalIgnoreCase)
            || origin.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyCorsHeaders(HttpListenerResponse response, string? origin)
    {
        // 回显具体来源而非 "*"，配合 Vary 避免中间层串缓存。
        response.Headers["Access-Control-Allow-Origin"] = string.IsNullOrWhiteSpace(origin) ? "*" : origin;
        response.Headers["Vary"] = "Origin";
    }

    private static void Respond(HttpListenerResponse response, int statusCode)
    {
        response.StatusCode = statusCode;
        response.Close();
    }

    public void Dispose()
    {
        Stop();
    }

    private sealed class NoteRequest
    {
        public string Url { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }
}
