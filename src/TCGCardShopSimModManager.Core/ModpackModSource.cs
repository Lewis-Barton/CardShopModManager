using System.Net.Http;

namespace TCGCardShopSimModManager.Core;

/// <summary>
/// Picks the right backend for each mod in a hosted pack: a direct
/// <see cref="ModEntry.DownloadUrl"/>, a Nexus id, or a pack-level fallback
/// source. Downstream everything is the same <see cref="IModSource"/> pipeline,
/// so caching, Range resume and retries are unchanged.
/// </summary>
public sealed class ModpackModSource : IModSource
{
    private readonly string _gameDomain;
    private readonly IModSource _fallback;
    private readonly NexusAuth _auth;
    private readonly HttpClient? _http;

    public ModpackModSource(
        string gameDomain,
        IModSource fallback,
        NexusAuth? auth = null,
        HttpClient? http = null)
    {
        _gameDomain = gameDomain;
        _fallback = fallback;
        _auth = auth ?? NexusAuth.Unified(http);
        _http = http;
    }

    public async Task<DownloadStream> OpenAsync(
        ModReference mod, long? resumeFromByte, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(mod.DownloadUrl))
            return await new HttpModSource(_ => mod.DownloadUrl!, _http)
                .OpenAsync(mod, resumeFromByte, cancellationToken);

        if (mod.NexusModId is not null)
            return await new NexusModSource(
                    NexusApi.ApiBaseUrl(), _gameDomain, _auth, _http)
                .OpenAsync(mod, resumeFromByte, cancellationToken);

        return await _fallback.OpenAsync(mod, resumeFromByte, cancellationToken);
    }
}
