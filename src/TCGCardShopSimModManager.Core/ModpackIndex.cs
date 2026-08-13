using System.Net.Http;
using System.Text.Json;

namespace TCGCardShopSimModManager.Core;

/// <summary>
/// One pack as listed in modpacks/index.json. <see cref="Logo"/> and
/// <see cref="Manifest"/> are repo-relative paths, resolved against the index
/// base URL when the app fetches them.
/// </summary>
public sealed record ModpackSummary(
    string Id,
    string Name,
    string ShortDescription,
    string Logo,
    string Manifest,
    string Version,
    string? Updated = null,
    /// <summary>
    /// Optional pack-level archive source used when a mod lists neither a
    /// <see cref="ModEntry.DownloadUrl"/> nor a Nexus id. An http(s) URL is used
    /// as a base; anything else is treated as a local folder.
    /// </summary>
    string? Source = null,
    /// <summary>
    /// Legacy ids this pack used to publish under. After a pack id rename the
    /// installed-version journal may still hold the old id; matching against
    /// these aliases keeps update detection and tracking working (BUG-009).
    /// </summary>
    List<string>? FormerIds = null)
{
    /// <summary>True when <paramref name="id"/> equals this pack's canonical id
    /// or any of its legacy <see cref="FormerIds"/>, case-insensitively.</summary>
    public bool IsId(string id) =>
        Id.Equals(id, StringComparison.OrdinalIgnoreCase) ||
        FormerIds?.Any(f => f.Equals(id, StringComparison.OrdinalIgnoreCase)) == true;
}

/// <summary>The modpacks/index.json document.</summary>
public sealed record ModpackIndex(int Version, List<ModpackSummary> Packs);

/// <summary>
/// Where the hosted modpack index lives. One constant so moving the repo only
/// touches a single line.
/// </summary>
public static class ModpackCatalog
{
    public const string DefaultIndexBaseUrl =
        "https://raw.githubusercontent.com/Lewis-Barton/TCGCardShopSimModManager/main/modpacks/";
}

/// <summary>
/// Fetches and parses the hosted modpack index, and resolves the absolute URLs
/// for each pack's logo and manifest.
/// </summary>
public sealed class ModpackIndexReader
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;

    public ModpackIndexReader(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<ModpackIndex> FetchIndexAsync(string? baseUrl = null, CancellationToken cancellationToken = default)
    {
        var url = Combine(baseUrl ?? ModpackCatalog.DefaultIndexBaseUrl, "index.json");
        var json = await _http.GetStringAsync(url, cancellationToken);
        return JsonSerializer.Deserialize<ModpackIndex>(json, Options)
            ?? throw new InvalidOperationException($"Failed to parse modpack index: {url}");
    }

    public string LogoUrl(ModpackSummary summary, string? baseUrl = null) =>
        Combine(baseUrl ?? ModpackCatalog.DefaultIndexBaseUrl, summary.Logo);

    public string ManifestUrl(ModpackSummary summary, string? baseUrl = null) =>
        Combine(baseUrl ?? ModpackCatalog.DefaultIndexBaseUrl, summary.Manifest);

    public async Task<ModListManifest> FetchManifestAsync(
        ModpackSummary summary, string? baseUrl = null, CancellationToken cancellationToken = default)
    {
        var json = await _http.GetStringAsync(ManifestUrl(summary, baseUrl), cancellationToken);
        var manifest = JsonSerializer.Deserialize<ModListManifest>(json, Options)
            ?? throw new InvalidOperationException($"Failed to parse manifest for pack '{summary.Id}'.");

        // Mirror ManifestReader: a manifest that omits dependencies/conflicts
        // deserialises those lists as null; treat "not declared" as "empty".
        return manifest with
        {
            Mods = manifest.Mods
                .Select(m => m with
                {
                    Dependencies = m.Dependencies ?? new List<string>(),
                    Conflicts = m.Conflicts ?? new List<string>()
                })
                .ToList()
        };
    }

    private static string Combine(string baseUrl, string relative) =>
        baseUrl.TrimEnd('/') + "/" + relative.TrimStart('/');
}
