using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace TCGCardShopSimModManager.Core;

public sealed record NexusUser(long UserId, string Name, bool IsPremium);

/// <summary>
/// A thin client for the Nexus Mods v1 API — validate the key, list a mod's
/// files, and ask for an authenticated download URI. Callers stay behind the
/// <see cref="IModSource"/> boundary: they never see Nexus types.
/// </summary>
public sealed class NexusApi
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;    // e.g. https://api.nexusmods.com/v1
    private readonly string _gameDomain; // e.g. tcgcardshopsimulator
    private readonly string _userAgent;

    // Single source of truth for how we identify to Nexus. Update to the
    // registered-app UA once the app is registered with Nexus.
    public const string UserAgent = "TCGCardShopSimModManager";
    public const string GameDomain = "tcgcardshopsimulator";

    /// <summary>The Nexus v1 API root. <see cref="ApiBaseUrl"/> honours NEXUS_API_BASE.</summary>
    public const string DefaultApiBaseUrl = "https://api.nexusmods.com/v1";

    /// <summary>API base URL, overridable with the NEXUS_API_BASE environment variable.</summary>
    public static string ApiBaseUrl() =>
        Environment.GetEnvironmentVariable("NEXUS_API_BASE") ?? DefaultApiBaseUrl;

    public NexusApi(string baseUrl, string gameDomain, string userAgent, HttpClient? http = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _gameDomain = gameDomain;
        _userAgent = userAgent;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>Where a user lands to download a file manually (free flow).</summary>
    public string FilePageUrl(long modId) =>
        $"https://www.nexusmods.com/{_gameDomain}/mods/{modId}?tab=files";

    public async Task<NexusUser> GetUserAsync(string apiKey, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync("/users/validate.json", apiKey, cancellationToken);
        var root = document.RootElement;

        return new NexusUser(
            root.TryGetProperty("user_id", out var id) ? id.GetInt64() : 0,
            root.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
            root.TryGetProperty("is_premium", out var premium) ? IsPremium(premium) : false);
    }

    /// <summary>
    /// Find the file id whose file_name matches. Used when the manifest knows
    /// the mod but not the exact file id.
    /// </summary>
    public async Task<long> ResolveFileIdAsync(long modId, string expectedFileName, string apiKey, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync($"/games/{_gameDomain}/mods/{modId}/files.json", apiKey, cancellationToken);

        foreach (var file in document.RootElement.EnumerateArray())
        {
            var fileName = file.TryGetProperty("file_name", out var name) ? name.GetString() : null;
            var fileId = file.TryGetProperty("file_id", out var id) ? (long?)id.GetInt64() : null;

            if (fileId is not null && fileName?.Equals(expectedFileName, StringComparison.OrdinalIgnoreCase) == true)
                return fileId.Value;
        }

        throw new DownloadException(
            $"No Nexus file named '{expectedFileName}' found for mod {modId}.",
            retryable: false);
    }

    /// <summary>Ask Nexus for the authenticated download URI for a specific file.</summary>
    public async Task<Uri> GetDownloadUriAsync(long modId, long fileId, string apiKey, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(
            $"/games/{_gameDomain}/mods/{modId}/files/{fileId}/download_link.json",
            apiKey,
            cancellationToken);

        foreach (var entry in document.RootElement.EnumerateArray())
        {
            if (entry.TryGetProperty("URI", out var uri) && uri.GetString() is { Length: > 0 } value &&
                Uri.TryCreate(value, UriKind.Absolute, out var link))
                return link;
        }

        throw new DownloadException($"Nexus returned no download URI for file {fileId}.", retryable: false);
    }

    private async Task<JsonDocument> GetJsonAsync(string path, string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}{path}");
        request.Headers.TryAddWithoutValidation("apikey", apiKey);
        request.Headers.TryAddWithoutValidation("User-Agent", _userAgent);

        using var response = await _http.SendAsync(request, cancellationToken);

        if ((int)response.StatusCode == 429)
        {
            // Rate limited — honor the retry window when the API tells us one.
            var seconds = response.Headers.RetryAfter?.Delta is { } delta
                ? (int)Math.Max(1, delta.TotalSeconds)
                : 60;
            throw new DownloadException($"Nexus rate limit reached; retry after {seconds}s.", retryable: true, retryAfterSeconds: seconds);
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new DownloadException("Nexus rejected the API key (403).", retryable: false);

        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new DownloadException(
                "The Nexus resource was not found — the mod may be archived or the ids in the manifest are wrong/outdated.",
                retryable: false);

        if (!response.IsSuccessStatusCode)
            throw new DownloadException($"Nexus returned {(int)response.StatusCode}.", retryable: (int)response.StatusCode >= 500);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    /// <summary>Nexus's v1 API reports booleans as strings like "true" — handle both.</summary>
    private static bool IsPremium(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => element.GetString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true,
            _ => false
        };
}