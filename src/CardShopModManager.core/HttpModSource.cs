using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace CardShopModManager.Core;

/// <summary>
/// Fetch a mod file over HTTP(S). A URL factory maps a mod to where it lives;
/// in the future NexusModSource will be a richer implementation with the same
/// contract (plus authentication and rate-limit handling).
/// </summary>
public sealed class HttpModSource : IModSource
{
    private static readonly HttpClient SharedHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(100)
    };

    private readonly HttpClient _http;
    private readonly Func<ModReference, string> _urlFactory;

    public HttpModSource(Func<ModReference, string> urlFactory, HttpClient? http = null)
    {
        _urlFactory = urlFactory;
        _http = http ?? SharedHttp;
    }

    public async Task<DownloadStream> OpenAsync(ModReference mod, long? resumeFromByte, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _urlFactory(mod));
        if (resumeFromByte is long from)
            request.Headers.Range = new RangeHeaderValue(from, null);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DownloadException("Request timed out.", retryable: true);
        }
        catch (HttpRequestException ex)
        {
            throw new DownloadException($"Connection failed: {ex.Message}", retryable: true);
        }

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // The server doesn't accept a resume after all; fall back to a fresh
            // start on the next attempt (the downloader cleans the partial file).
            response.Dispose();
            throw new DownloadException("The server rejected the resume request.", retryable: true);
        }

        if (!response.IsSuccessStatusCode)
        {
            var retryable = (int)response.StatusCode >= 500;
            response.Dispose();
            throw new DownloadException(
                retryable
                    ? $"Server returned {(int)response.StatusCode}."
                    : $"Source returned {(int)response.StatusCode}.",
                retryable);
        }

        var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        var totalBytes = response.Content.Headers.ContentLength;
        long startOffset = 0;

        if (response.StatusCode == HttpStatusCode.PartialContent &&
            response.Content.Headers.ContentRange is { } range)
        {
            totalBytes = range.Length;
            startOffset = range.From ?? resumeFromByte ?? 0;
        }

        return new DownloadStream(totalBytes, startOffset, content, response);
    }
}