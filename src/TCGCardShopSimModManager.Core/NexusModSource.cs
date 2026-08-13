using System.Net.Http;

namespace TCGCardShopSimModManager.Core;

/// <summary>
/// A Nexus Mods backend behind the same <see cref="IModSource"/> contract as
/// everything else: resolve the manifest's Nexus ids to an authenticated
/// download URI, then hand the bytes to a plain HTTP source (which already
/// knows streaming and Range resumes). Nothing downstream knows Nexus exists.
///
/// Free accounts cannot be auto-downloaded — Nexus only hands premium users a
/// direct URI, so the free flow tells the user where to grab the file manually.
/// </summary>
public sealed class NexusModSource : IModSource
{
    private readonly NexusApi _api;
    private readonly HttpClient _http;
    private readonly NexusAuth _auth;

    public NexusModSource(
        string apiBaseUrl,
        string gameDomain,
        NexusAuth auth,
        HttpClient? http = null,
        string userAgent = NexusApi.UserAgent)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(100) };
        _api = new NexusApi(apiBaseUrl, gameDomain, userAgent, _http);
        _auth = auth;
    }

    public async Task<DownloadStream> OpenAsync(ModReference mod, long? resumeFromByte, CancellationToken cancellationToken)
    {
        var modId = mod.NexusModId
            ?? throw new DownloadException(
                "This mod has no nexusModId in the manifest — a Nexus source cannot resolve it.",
                retryable: false);

        // OAuth carries the user in the token (no extra call); the API-key path
        // validates the key and reports premium via the API.
        var user = _auth.User
            ?? await _api.GetUserAsync(_auth, cancellationToken);

        if (!user.IsPremium)
        {
            throw new DownloadException(
                $"Your Nexus account is not premium, so '{mod.FileName}' cannot be downloaded automatically. " +
                $"Download it from {_api.FilePageUrl(modId)} and place it in the source folder.",
                retryable: false);
        }

        var fileId = mod.NexusFileId ??
            await _api.ResolveFileIdAsync(modId, mod.FileName, _auth, cancellationToken);

        var uri = await _api.GetDownloadUriAsync(modId, fileId, _auth, cancellationToken);

        // The bytes themselves come from Nexus's CDN link, fetched by the plain
        // HTTP source — same streaming, same Range resume, no Nexus-specific code.
        var inner = new HttpModSource(_ => uri.AbsoluteUri, _http);
        return await inner.OpenAsync(mod, resumeFromByte, cancellationToken);
    }
}