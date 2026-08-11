using System.Net.Http;

namespace CardShopModManager.Core;

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
    private readonly Func<string?> _apiKeyProvider;

    public NexusModSource(
        string apiBaseUrl,
        string gameDomain,
        Func<string?> apiKeyProvider,
        HttpClient? http = null,
        string userAgent = "CardShopModManager/dev")
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(100) };
        _api = new NexusApi(apiBaseUrl, gameDomain, userAgent, _http);
        _apiKeyProvider = apiKeyProvider;
    }

    public async Task<DownloadStream> OpenAsync(ModReference mod, long? resumeFromByte, CancellationToken cancellationToken)
    {
        var modId = mod.NexusModId
            ?? throw new DownloadException(
                "This mod has no nexusModId in the manifest — a Nexus source cannot resolve it.",
                retryable: false);

        var apiKey = _apiKeyProvider()
            ?? throw new DownloadException(
                "No Nexus API key stored. Run 'nexus set-key <apikey>' first.",
                retryable: false);

        var user = await _api.GetUserAsync(apiKey, cancellationToken);

        if (!user.IsPremium)
        {
            throw new DownloadException(
                $"Your Nexus account is not premium, so '{mod.FileName}' cannot be downloaded automatically. " +
                $"Download it from {_api.FilePageUrl(modId)} and place it in the source folder.",
                retryable: false);
        }

        var fileId = mod.NexusFileId ??
            await _api.ResolveFileIdAsync(modId, mod.FileName, apiKey, cancellationToken);

        var uri = await _api.GetDownloadUriAsync(modId, fileId, apiKey, cancellationToken);

        // The bytes themselves come from Nexus's CDN link, fetched by the plain
        // HTTP source — same streaming, same Range resume, no Nexus-specific code.
        var inner = new HttpModSource(_ => uri.AbsoluteUri, _http);
        return await inner.OpenAsync(mod, resumeFromByte, cancellationToken);
    }
}