using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace TCGCardShopSimModManager.Core;

public sealed record UpdateCheckResult(
    bool HasRelease,
    bool IsUpToDate,
    string? LatestVersion,
    string? ReleaseUrl,
    string? Error);

/// <summary>
/// Compares the running version against the newest GitHub release tag for this
/// project. Runs only when explicitly invoked.
/// </summary>
public sealed class UpdateChecker
{
    private readonly string _repo;          // e.g. Lewis-Barton/TCGCardShopSimModManager
    private readonly string _localVersion;  // e.g. 0.1.0
    private readonly HttpClient _http;

    public UpdateChecker(string repo, string localVersion, HttpClient? http = null)
    {
        _repo = repo;
        _localVersion = localVersion;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.github.com/repos/{_repo}/releases/latest");
            request.Headers.TryAddWithoutValidation("User-Agent", "TCGCardShopSimModManager");
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");

            using var response = await _http.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return new UpdateCheckResult(false, true, null, null, null);

            if (!response.IsSuccessStatusCode)
                return new UpdateCheckResult(false, true, null, null,
                    $"GitHub returned {(int)response.StatusCode}.");

            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            var releaseUrl = root.TryGetProperty("html_url", out var html) ? html.GetString() : null;

            var latest = tag.TrimStart('v');
            var upToDate = CompareVersions(_localVersion, latest) >= 0;

            return new UpdateCheckResult(true, upToDate, latest, releaseUrl, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new UpdateCheckResult(false, true, null, null,
                $"Could not reach GitHub: {ex.Message}");
        }
    }

    private static int CompareVersions(string a, string b)
    {
        var aParts = a.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var bParts = b.Split('.', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < Math.Max(aParts.Length, bParts.Length); i++)
        {
            var aNum = i < aParts.Length && int.TryParse(aParts[i], out var x) ? x : 0;
            var bNum = i < bParts.Length && int.TryParse(bParts[i], out var y) ? y : 0;
            if (aNum != bNum)
                return aNum.CompareTo(bNum);
        }

        return 0;
    }
}
