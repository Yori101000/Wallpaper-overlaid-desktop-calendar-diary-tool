using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using TransparentCalendar.Models;

namespace TransparentCalendar.Services;

public sealed class NoteListenerService : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly StorageService _storage;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private bool _running;
    private int _port;

    public int Port => _port;
    public bool IsRunning => _running;

    public event Action? OnNoteReceived;

    public NoteListenerService(StorageService storage)
    {
        _storage = storage;
    }

    public bool Start(int port = 51999)
    {
        try
        {
            _port = port;
            _listener.Prefixes.Clear();
            _listener.Prefixes.Add($"http://localhost:{_port}/");
            _listener.Start();
            _running = true;
            _ = ListenLoop();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Stop()
    {
        _running = false;
        try { _listener.Stop(); } catch { }
    }

    private async System.Threading.Tasks.Task ListenLoop()
    {
        while (_running)
        {
            try
            {
                var ctx = await _listener.GetContextAsync();
                _ = HandleRequest(ctx);
            }
            catch { break; }
        }
    }

    private async System.Threading.Tasks.Task HandleRequest(HttpListenerContext ctx)
    {
        try
        {
            var request = ctx.Request;
            var response = ctx.Response;

            if (request.HttpMethod == "OPTIONS")
            {
                // CORS preflight
                response.Headers.Add("Access-Control-Allow-Origin", "*");
                response.Headers.Add("Access-Control-Allow-Methods", "POST, OPTIONS");
                response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
                response.StatusCode = 204;
                response.Close();
                return;
            }

            if (request.HttpMethod == "POST" && request.Url?.AbsolutePath == "/save")
            {
                string body;
                using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
                {
                    body = await reader.ReadToEndAsync();
                }

                var data = JsonSerializer.Deserialize<NoteRequest>(body, _jsonOptions);
                if (data != null && !string.IsNullOrWhiteSpace(data.Url))
                {
                    var notes = _storage.LoadWebNotes();
                    var title = string.IsNullOrWhiteSpace(data.Title) ? ExtractDomain(data.Url) : data.Title;
                    var existing = notes.Find(n => n.Url == data.Url);

                    if (existing != null)
                    {
                        if (!string.IsNullOrWhiteSpace(data.Text))
                            existing.Notes.Add(data.Text);
                        existing.UpdatedAt = DateTime.Now;
                    }
                    else
                    {
                        notes.Add(new WebNoteGroup
                        {
                            Id = Guid.NewGuid().ToString(),
                            Title = title,
                            Url = data.Url,
                            Notes = string.IsNullOrWhiteSpace(data.Text) ? [] : new System.Collections.Generic.List<string> { data.Text },
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now
                        });
                    }

                    _storage.SaveWebNotes(notes);

                    response.Headers.Add("Access-Control-Allow-Origin", "*");
                    response.ContentType = "application/json";
                    var ok = Encoding.UTF8.GetBytes("{\"ok\":true}");
                    response.ContentLength64 = ok.Length;
                    await response.OutputStream.WriteAsync(ok);
                    response.Close();

                    OnNoteReceived?.Invoke();
                    return;
                }
            }

            response.StatusCode = 400;
            response.Close();
        }
        catch { }
    }

    private static string ExtractDomain(string url)
    {
        try
        {
            var uri = new Uri(url);
            return uri.Host;
        }
        catch
        {
            return url;
        }
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
