using System;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using RabbitEars.Live;
using RabbitEars.Tv;

namespace RabbitEars.Web;

/// <summary>
/// The viewer: a System.Net.HttpListener bound to 127.0.0.1 that serves the page, the JSON API and the
/// /ws/video WebSocket (one binary message per decoded frame: <see cref="FramePacket"/>).
/// </summary>
public sealed class WebServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly LiveSession _session;
    private readonly LiveApi _api;
    private readonly byte[] _page;
    private readonly CancellationTokenSource _stop = new();

    public WebServer(LiveSession session, int port)
    {
        _session = session;
        _api = new LiveApi(session);
        _page = LoadPage();
        Url = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(Url);
        _listener.Start();
        new Thread(AcceptLoop) { IsBackground = true, Name = "web-accept" }.Start();
    }

    public string Url { get; }

    private static byte[] LoadPage()
    {
        using Stream stream = typeof(WebServer).Assembly.GetManifestResourceStream("RabbitEars.Web.page.html")
                              ?? throw new InvalidOperationException("embedded page.html is missing");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private void AcceptLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = _listener.GetContext();
            }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }
            if (context.Request.IsWebSocketRequest && context.Request.Url?.AbsolutePath == "/ws/video")
                new Thread(() => ServeVideo(context)) { IsBackground = true, Name = "web-video" }.Start();
            else
                ThreadPool.QueueUserWorkItem(_ => ServeRequest(context));
        }
    }

    private void ServeRequest(HttpListenerContext context)
    {
        try
        {
            string path = context.Request.Url?.AbsolutePath ?? "/";
            if (path == "/")
            {
                Respond(context, 200, "text/html; charset=utf-8", _page);
                return;
            }
            if (path == "/api/frame")
            {
                Respond(context, 200, "application/octet-stream", _session.Hub.Latest?.Bytes ?? Array.Empty<byte>());
                return;
            }
            try
            {
                object? answer = _api.Handle(path, context.Request.QueryString);
                if (answer is null) Respond(context, 404, "text/plain", Encoding.UTF8.GetBytes("not found"));
                else Respond(context, 200, "application/json", JsonSerializer.SerializeToUtf8Bytes(answer));
            }
            catch (ArgumentException e)
            {
                Respond(context, 400, "application/json", JsonSerializer.SerializeToUtf8Bytes(new { error = e.Message }));
            }
        }
        catch (Exception e) when (e is HttpListenerException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            // the browser went away mid-response
        }
    }

    private static void Respond(HttpListenerContext context, int status, string contentType, byte[] body)
    {
        HttpListenerResponse response = context.Response;
        response.StatusCode = status;
        response.ContentType = contentType;
        response.Headers["Cache-Control"] = "no-store";
        response.ContentLength64 = body.Length;
        response.OutputStream.Write(body, 0, body.Length);
        response.Close();
    }

    /// <summary>One viewer: always sends the newest frame; a viewer slower than the decoder simply skips frames.</summary>
    private void ServeVideo(HttpListenerContext context)
    {
        WebSocket? socket = null;
        _session.ViewerChanged(+1);
        try
        {
            socket = context.AcceptWebSocketAsync(null).GetAwaiter().GetResult().WebSocket;
            WebSocket open = socket;
            var incoming = new byte[256];
            // Reading is what notices the close handshake; the viewer never sends anything else.
            var closed = open.ReceiveAsync(new ArraySegment<byte>(incoming), _stop.Token);
            uint sequence = 0;
            while (!_stop.IsCancellationRequested && open.State == WebSocketState.Open && !closed.IsCompleted)
            {
                FramePacket? frame = _session.Hub.WaitNext(sequence, 500);
                if (frame is null) continue;
                sequence = frame.Sequence;
                open.SendAsync(new ArraySegment<byte>(frame.Bytes), WebSocketMessageType.Binary, true, _stop.Token).GetAwaiter().GetResult();
            }
        }
        catch (Exception e) when (e is WebSocketException or HttpListenerException or IOException or OperationCanceledException
                                      or ObjectDisposedException or InvalidOperationException)
        {
            // viewer closed the tab
        }
        finally
        {
            _session.ViewerChanged(-1);
            socket?.Abort();
            socket?.Dispose();
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch (Exception e) when (e is HttpListenerException or ObjectDisposedException)
        {
        }
    }
}
