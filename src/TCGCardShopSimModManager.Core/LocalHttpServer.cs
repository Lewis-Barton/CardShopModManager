using System.Collections.Concurrent;
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
    string? RetryAfter = null,
    string? FilePath = null,
    long FileOffset = 0);

/// <summary>
/// A tiny in-process HTTP server built on TcpListener. Not a product feature —
/// it exists so tests can exercise the real HTTP code path (ranges, retries,
/// corruption) against a server we control, and so the CLI's <c>serve</c>
/// command can demo a download without needing a real web server.
/// </summary>
public sealed class LocalHttpServer : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<int, Task> _clientTasks = new();
    private readonly Task _loop;
    private int _nextClientId;
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

            var clientId = Interlocked.Increment(ref _nextClientId);
            var task = HandleClient(client, _cts.Token);
            _clientTasks[clientId] = task;
            _ = task.ContinueWith(
                completedTask => _clientTasks.TryRemove(clientId, out _),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            if (task.IsCompleted)
                _clientTasks.TryRemove(clientId, out _);
        }
    }

    private async Task HandleClient(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using var netStream = client.GetStream();
            var head = await ReadHeadAsync(netStream, cancellationToken);
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

            await netStream.WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()), cancellationToken);
            if (response.FilePath is not null)
                await WriteFileAsync(netStream, response, contentLength, cancellationToken);
            else
                await netStream.WriteAsync(response.Body, cancellationToken);
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

    private static async Task<byte[]> ReadHeadAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        // Read until the "\r\n\r\n" that ends the request head, capped at 8 KiB.
        const int maxHeadBytes = 8192;
        using var head = new MemoryStream(maxHeadBytes);
        var chunk = new byte[1024];
        while (head.Length < maxHeadBytes)
        {
            var remaining = maxHeadBytes - (int)head.Length;
            var read = await stream.ReadAsync(
                chunk.AsMemory(0, Math.Min(chunk.Length, remaining)), cancellationToken);
            if (read == 0)
                break;

            head.Write(chunk, 0, read);
            if (FindHeaderEnd(head.GetBuffer(), (int)head.Length) >= 0)
                return head.ToArray();
        }

        return Array.Empty<byte>();
    }

    private static int FindHeaderEnd(byte[] bytes, int length)
    {
        for (var i = Math.Max(0, length - 1027); i <= length - 4; i++)
        {
            if (bytes[i] == 13 && bytes[i + 1] == 10 &&
                bytes[i + 2] == 13 && bytes[i + 3] == 10)
                return i + 4;
        }

        return -1;
    }

    private static async Task WriteFileAsync(
        NetworkStream destination,
        HttpResponse response,
        long contentLength,
        CancellationToken cancellationToken)
    {
        await using var file = new FileStream(
            response.FilePath!, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        file.Seek(response.FileOffset, SeekOrigin.Begin);

        var buffer = new byte[64 * 1024];
        var remaining = contentLength;
        while (remaining > 0)
        {
            var read = await file.ReadAsync(
                buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken);
            if (read == 0)
                break;
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            remaining -= read;
        }
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
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return request =>
        {
            var relative = Uri.UnescapeDataString(request.Path.TrimStart('/'))
                .Replace('/', Path.DirectorySeparatorChar);
            var path = Path.GetFullPath(Path.Combine(fullRoot, relative));
            if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                return new HttpResponse(404, Array.Empty<byte>(), null);
            if (!File.Exists(path))
                return new HttpResponse(404, Array.Empty<byte>(), null);

            var length = new FileInfo(path).Length;
            if (request.RangeStart is long start)
            {
                if (start >= length)
                    return new HttpResponse(416, Array.Empty<byte>(), $"bytes */{length}");

                return new HttpResponse(
                    206, Array.Empty<byte>(), $"bytes {start}-{length - 1}/{length}",
                    length - start, FilePath: path, FileOffset: start);
            }

            return new HttpResponse(200, Array.Empty<byte>(), null, length, FilePath: path);
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
            _loop.Wait(TimeSpan.FromSeconds(2));
            Task.WhenAll(_clientTasks.Values).Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Shutdown timing — nothing else to clean.
        }

        _cts.Dispose();
    }
}
