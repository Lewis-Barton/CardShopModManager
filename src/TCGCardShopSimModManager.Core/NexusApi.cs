using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace TCGCardShopSimModManager.Core;

public sealed record NexusUser(long UserId, string Name, bool IsPremium);
public sealed record NexusModInfo(long ModId, string Name);
public sealed record NexusFileInfo(
    long FileId,
    string FileName,
    string? Version,
    long? SizeBytes,
    string? DisplayName = null,
    string? Category = null);

/// <summary>
/// A thin client for the Nexus Mods v1 API — validate the key, list a mod's
/// files, and ask for an authenticated download URI. Callers stay behind the
/// <see cref="IModSource"/> boundary: they never see Nexus types.
/// </summary>
public sealed class NexusApi : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
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
        _ownsHttp = http is null;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>Where a user lands to download a file manually (free flow).</summary>
    public string FilePageUrl(long modId) =>
        $"https://www.nexusmods.com/{_gameDomain}/mods/{modId}?tab=files";

    public async Task<NexusUser> GetUserAsync(NexusAuth auth, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync("/users/validate.json", auth, cancellationToken);
        var root = document.RootElement;

        return new NexusUser(
            root.TryGetProperty("user_id", out var id) ? id.GetInt64() : 0,
            root.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
            root.TryGetProperty("is_premium", out var premium) ? IsPremium(premium) : false);
    }

    public async Task<NexusModInfo> GetModInfoAsync(long modId, NexusAuth auth, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(
            $"/games/{_gameDomain}/mods/{modId}.json", auth, cancellationToken);
        var root = document.RootElement;
        var name = root.TryGetProperty("name", out var value) ? value.GetString() : null;
        if (string.IsNullOrWhiteSpace(name))
            throw new DownloadException($"Nexus returned no name for mod {modId}.", retryable: false);
        return new NexusModInfo(modId, name);
    }

    public async Task<NexusFileInfo> GetFileInfoAsync(
        long modId, long fileId, NexusAuth auth, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(
            $"/games/{_gameDomain}/mods/{modId}/files/{fileId}.json", auth, cancellationToken);
        var root = document.RootElement;
        var fileName = root.TryGetProperty("file_name", out var name) ? name.GetString() : null;
        if (string.IsNullOrWhiteSpace(fileName))
            throw new DownloadException($"Nexus returned no filename for file {fileId}.", retryable: false);

        var version = root.TryGetProperty("version", out var versionValue)
            ? versionValue.GetString()
            : null;
        var size = root.TryGetProperty("size_in_bytes", out var sizeValue) && sizeValue.TryGetInt64(out var bytes)
            ? bytes
            : (long?)null;
        var displayName = root.TryGetProperty("name", out var displayNameValue)
            ? displayNameValue.GetString()
            : null;
        var category = root.TryGetProperty("category_name", out var categoryValue)
            ? categoryValue.GetString()
            : null;
        return new NexusFileInfo(fileId, fileName, version, size, displayName, category);
    }

    public async Task<IReadOnlyList<NexusFileInfo>> ListFilesAsync(
        long modId, NexusAuth auth, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(
            $"/games/{_gameDomain}/mods/{modId}/files.json", auth, cancellationToken);
        var root = document.RootElement;
        var files = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("files", out var nested) && nested.ValueKind == JsonValueKind.Array
                ? nested
                : throw new DownloadException(
                    $"Nexus returned an invalid file list for mod {modId}.", retryable: false);

        var result = new List<NexusFileInfo>();
        foreach (var file in files.EnumerateArray())
        {
            if (!file.TryGetProperty("file_id", out var idValue) || !idValue.TryGetInt64(out var fileId) || fileId <= 0)
                continue;
            var fileName = file.TryGetProperty("file_name", out var nameValue) ? nameValue.GetString() : null;
            if (string.IsNullOrWhiteSpace(fileName))
                continue;
            var version = file.TryGetProperty("version", out var versionValue) ? versionValue.GetString() : null;
            var size = file.TryGetProperty("size_in_bytes", out var sizeValue) && sizeValue.TryGetInt64(out var bytes)
                ? bytes
                : (long?)null;
            var displayName = file.TryGetProperty("name", out var displayNameValue)
                ? displayNameValue.GetString()
                : null;
            var category = file.TryGetProperty("category_name", out var categoryValue)
                ? categoryValue.GetString()
                : null;
            result.Add(new NexusFileInfo(fileId, fileName, version, size, displayName, category));
        }

        return result;
    }

    /// <summary>
    /// Find the file id whose file_name matches. Used when the manifest knows
    /// the mod but not the exact file id.
    /// </summary>
    public async Task<long> ResolveFileIdAsync(long modId, string expectedFileName, NexusAuth auth, CancellationToken cancellationToken)
    {
        foreach (var file in await ListFilesAsync(modId, auth, cancellationToken))
        {
            if (file.FileName.Equals(expectedFileName, StringComparison.OrdinalIgnoreCase))
                return file.FileId;
        }

        throw new DownloadException(
            $"No Nexus file named '{expectedFileName}' found for mod {modId}.",
            retryable: false);
    }

    /// <summary>Ask Nexus for the authenticated download URI for a specific file.</summary>
    public async Task<Uri> GetDownloadUriAsync(long modId, long fileId, NexusAuth auth, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(
            $"/games/{_gameDomain}/mods/{modId}/files/{fileId}/download_link.json",
            auth,
            cancellationToken);

        foreach (var entry in document.RootElement.EnumerateArray())
        {
            if (entry.TryGetProperty("URI", out var uri) && uri.GetString() is { Length: > 0 } value &&
                Uri.TryCreate(value, UriKind.Absolute, out var link))
                return link;
        }

        throw new DownloadException($"Nexus returned no download URI for file {fileId}.", retryable: false);
    }

    private async Task<JsonDocument> GetJsonAsync(string path, NexusAuth auth, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}{path}");
        request.Headers.TryAddWithoutValidation(auth.HeaderName, await auth.GetHeaderValueAsync());
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

    public void Dispose()
    {
        if (_ownsHttp)
            _http.Dispose();
    }
}
