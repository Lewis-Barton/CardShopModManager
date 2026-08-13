using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TCGCardShopSimModManager.Core;

/// <summary>
/// A minimal loopback HTTP listener for catching the OAuth redirect. Uses a raw
/// <see cref="TcpListener"/> (not <see cref="System.Net.HttpListener"/>) so we
/// don't need a Windows URL-ACL reservation to bind a port. The desktop guide
/// for Nexus OAuth recommends exactly this pattern.
/// </summary>
public sealed class LoopbackOAuthListener : IAsyncDisposable, IDisposable
{
    private readonly TcpListener _listener;
    private readonly string _redirectUri;

    public LoopbackOAuthListener(string redirectUri)
    {
        _redirectUri = redirectUri;
        var port = new Uri(redirectUri).Port;
        _listener = new TcpListener(IPAddress.Loopback, port);
    }

    public string RedirectUri => _redirectUri;

    public Task StartAsync()
    {
        _listener.Start();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Accepts one connection, reads the request line, extracts <c>code</c> and
    /// <c>state</c> from the query, and replies with a tiny HTML page so the
    /// browser can close. Returns the captured values.
    /// </summary>
    public async Task<(string Code, string State)> WaitForCallbackAsync(CancellationToken cancellationToken)
    {
        using var client = await _listener.AcceptTcpClientAsync(cancellationToken);
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

        string? code = null, state = null;

        var requestLine = await reader.ReadLineAsync(cancellationToken);
        if (requestLine is not null)
        {
            var queryStart = requestLine.IndexOf('?');
            if (queryStart >= 0)
            {
                var query = requestLine.Substring(queryStart + 1);
                var space = query.IndexOf(' ');
                if (space >= 0)
                    query = query.Substring(0, space);

                foreach (var pair in query.Split('&'))
                {
                    var kv = pair.Split('=', 2);
                    var key = Uri.UnescapeDataString(kv[0]);
                    var value = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
                    if (key == "code") code = value;
                    else if (key == "state") state = value;
                }
            }
        }

        const string html = "<!doctype html><html><body><h2>Nexus sign-in complete</h2>"
            + "<p>You can close this window and return to the mod manager.</p></body></html>";
        var body = Encoding.UTF8.GetBytes(html);
        var header = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n"
            + $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n");

        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        return (code ?? "", state ?? "");
    }

    public void Dispose() => _listener.Stop();

    public ValueTask DisposeAsync()
    {
        _listener.Stop();
        return ValueTask.CompletedTask;
    }
}
