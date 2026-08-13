using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TCGCardShopSimModManager.Core;

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
    /// Accepts connections until one carries <c>code</c> or <c>error</c>. Browsers
    /// often open stray requests first (e.g. /favicon.ico); those are answered and
    /// ignored so they don't swallow the real callback. Returns what was captured.
    /// </summary>
    public async Task<LoopbackCallback> WaitForCallbackAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var client = await _listener.AcceptTcpClientAsync(cancellationToken);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

            var result = ParseRequest(await reader.ReadLineAsync(cancellationToken));

            // Answer every connection so the browser doesn't hang; only the one
            // with code/error is the one we act on.
            await RespondAsync(stream, cancellationToken);

            if (result.Code is not null || result.Error is not null)
                return result;
        }

        return new LoopbackCallback(null, null, null, null, null);
    }

    private static LoopbackCallback ParseRequest(string? requestLine)
    {
        if (requestLine is null)
            return new LoopbackCallback(null, null, null, null, null);

        var queryStart = requestLine.IndexOf('?');
        if (queryStart < 0)
            return new LoopbackCallback(null, null, null, null, requestLine);

        var rest = requestLine.Substring(queryStart + 1);
        var space = rest.IndexOf(' ');
        if (space >= 0)
            rest = rest.Substring(0, space);

        string? code = null, state = null, error = null, errorDescription = null;
        foreach (var pair in rest.Split('&'))
        {
            var kv = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(kv[0]);
            var value = kv.Length > 1 ? DecodeQueryValue(kv[1]) : "";
            if (key == "code") code = value;
            else if (key == "state") state = value;
            else if (key == "error") error = value;
            else if (key == "error_description") errorDescription = value;
        }

        return new LoopbackCallback(code, state, error, errorDescription, rest);
    }

    /// <summary>Query values are application/x-www-form-urlencoded, so '+' is a space.</summary>
    private static string DecodeQueryValue(string value) =>
        Uri.UnescapeDataString(value.Replace("+", " "));

    private static async Task RespondAsync(Stream stream, CancellationToken cancellationToken)
    {
        const string html = "<!doctype html><html><body><h2>Nexus sign-in complete</h2>"
            + "<p>You can close this window and return to the mod manager.</p></body></html>";
        var body = Encoding.UTF8.GetBytes(html);
        var header = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n"
            + $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n");

        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public void Dispose() => _listener.Stop();

    public ValueTask DisposeAsync()
    {
        _listener.Stop();
        return ValueTask.CompletedTask;
    }
}

/// <summary>What the loopback redirect delivered.</summary>
public sealed record LoopbackCallback(string? Code, string? State, string? Error, string? ErrorDescription, string? RawQuery);
