using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TCGCardShopSimModManager.Core;

public sealed record HttpRequest(string Path, long? RangeStart);

public sealed record HttpResponse(
    int StatusCode,
    byte[] Body,
    string? ContentRange,
    long? ContentLengthOverride = null,
    string? RetryAfter = null);

/// <summary>
/// A tiny in-process HTTP server built on TcpListener. Not a product feature —
/// it exists so tests can exercise the real HTTP code path (ranges, retries,
/// corruption) against a server we control, and so the CLI's <c>serve</c>
/// command can demo a download without needing a real web server.
/// </summary>
public sealed class LocalHttpServer : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private TcpListener? _listener;
    private bool _disposed;

    public int Port { get; }

    /// <summary>Return the response for a request; default is 404.</summary>
    public Func<HttpRequest, HttpResponse> Provider { get; set; } =
        _ => new HttpResponse(404, Array.Empty<byte>(), null);

    public LocalHttpServer(int? requestedPort = null)
    {
        _listener = new TcpListener(IPAddress.Loopback, requestedPort ?? 0);
        try
        {
            _listener.Start();
        }
        catch
        {
            _listener = null;
            throw;
        }

        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _loop = Task.Run(() => AcceptLoop());
    }

    public string Url(string path) => $"http://localhost:{Port}/{path.TrimStart('/')}";

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                if (_cts.IsCancellationRequested)
                    break;
                continue;
            }

            _ = Task.Run(() => HandleClient(client));
        }
    }

    private async Task HandleClient(TcpClient client)
    {
        try
        {
            using var netStream = client.GetStream();
            var head = await ReadHeadAsync(netStream);
            if (head.Length == 0)
                return;

            var request = ParseRequest(head);
            HttpResponse response;
            try
            {
                response = Provider(request);
            }
            catch
            {
                response = new HttpResponse(500, Array.Empty<byte>(), null);
            }

            var contentLength = response.ContentLengthOverride ?? response.Body.LongLength;
            var sb = new StringBuilder();
            sb.Append(response.StatusCode == 206 ? "HTTP/1.1 206 Partial Content\r\n"
                     : $"HTTP/1.1 {response.StatusCode} OK\r\n");
            sb.Append($"Content-Length: {contentLength}\r\n");
            sb.Append("Accept-Ranges: bytes\r\n");
            sb.Append("Connection: close\r\n");
            if (response.ContentRange is not null)
                sb.Append($"Content-Range: {response.ContentRange}\r\n");
            if (response.RetryAfter is not null)
                sb.Append($"Retry-After: {response.RetryAfter}\r\n");
            sb.Append("\r\n");

            await netStream.WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()));
            await netStream.WriteAsync(response.Body);
        }
        catch
        {
            // Broken connections are normal (client cancelled mid-stream).
        }
        finally
        {
            client.Dispose();
        }
    }

    private static async Task<byte[]> ReadHeadAsync(NetworkStream stream)
    {
        // Read until the "\r\n\r\n" that ends the request head, capped at 8 KiB.
        using var buffer = new MemoryStream();
        var chunk = new byte[1];
        while (buffer.Length < 8192)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(0, 1));
            if (read == 0)
                break;

            buffer.WriteByte(chunk[0]);
            var bytes = buffer.ToArray();
            if (EndsWithDoubleCrlf(bytes))
                break;
        }

        return buffer.ToArray();
    }

    private static bool EndsWithDoubleCrlf(byte[] bytes)
    {
        if (bytes.Length < 4)
            return false;
        return bytes[^4] == 13 && bytes[^3] == 10 && bytes[^2] == 13 && bytes[^1] == 10;
    }

    private static HttpRequest ParseRequest(byte[] head)
    {
        var text = Encoding.ASCII.GetString(head);
        var lines = text.Replace("\r", "").Split('\n');
        var path = lines.Length > 0 && lines[0].Split(' ').Length > 1 ? lines[0].Split(' ')[1] : "/";

        long? rangeStart = null;
        var rangeLine = lines.FirstOrDefault(l => l.StartsWith("Range:", StringComparison.OrdinalIgnoreCase));
        if (rangeLine is not null)
        {
            var spec = rangeLine[(rangeLine.IndexOf('=') + 1)..];
            var dash = spec.IndexOf('-');
            if (dash > 0 && long.TryParse(spec[..dash], out var start))
                rangeStart = start;
        }

        return new HttpRequest(path, rangeStart);
    }

    /// <summary>Serves every file under <paramref name="root"/> with Range support.</summary>
    public static Func<HttpRequest, HttpResponse> FolderProvider(string root)
    {
        return request =>
        {
            var path = Path.Combine(root, request.Path.TrimStart('/'));
            if (!File.Exists(path))
                return new HttpResponse(404, Array.Empty<byte>(), null);

            var body = File.ReadAllBytes(path);
            if (request.RangeStart is long start)
            {
                if (start >= body.Length)
                    return new HttpResponse(416, Array.Empty<byte>(), $"bytes */{body.Length}");

                var slice = body.AsSpan((int)start).ToArray();
                return new HttpResponse(206, slice, $"bytes {start}-{body.Length - 1}/{body.Length}");
            }

            return new HttpResponse(200, body, null);
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _cts.Cancel();
        try
        {
            _listener?.Stop();
        }
        catch
        {
            // Already stopped.
        }

        try
        {
            _loop.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Shutdown timing — nothing else to clean.
        }

        _cts.Dispose();
    }
}