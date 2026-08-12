using System.Net.Http;

namespace TCGCardShopSimModManager.Core;

/// <summary>
/// Drives a hosted modpack end to end: download every archive (via the per-mod
/// source dispatcher) into a cache folder, then run the standard install
/// pipeline against that folder. The install half is exactly
/// <see cref="DeploymentService.Install"/> — validate, plan, refuse conflicts,
/// then copy.
/// </summary>
public sealed class ModpackInstaller
{
    private readonly string _gameFolderPath;
    private readonly HttpClient? _http;

    public ModpackInstaller(string gameFolderPath, HttpClient? http = null)
    {
        _gameFolderPath = gameFolderPath;
        _http = http;
    }

    public async Task<DeploymentReport> InstallAsync(
        ModListManifest manifest,
        IModSource? fallbackSource = null,
        string? cacheDirectory = null,
        CancellationToken cancellationToken = default)
    {
        cacheDirectory ??= Path.Combine(
            Path.GetTempPath(),
            "cardshopmodmanager-modpack",
            (manifest.Name ?? "pack").ToLowerInvariant());

        // The fallback only matters for mods with neither a DownloadUrl nor a
        // Nexus id; point it at the cache so an already-downloaded file is reused.
        var fallback = fallbackSource ?? new LocalFileSource(cacheDirectory);

        var source = new ModpackModSource(manifest.Game, fallback, http: _http);
        var downloader = new ModDownloader(source, new DownloadOptions { CacheDirectory = cacheDirectory });

        foreach (var entry in manifest.Mods)
        {
            var mod = new ModReference(
                entry.Id, entry.Archive, entry.Sha256, entry.Version,
                entry.NexusModId, entry.NexusFileId, entry.DownloadUrl);

            var result = await downloader.DownloadAsync(mod, cacheDirectory, cancellationToken: cancellationToken);
            if (!result.Success)
                return DeploymentReport.Failure(
                    new List<string>(), $"Failed to download {entry.Name}: {result.Error}");
        }

        return new DeploymentService().Install(manifest, cacheDirectory, _gameFolderPath);
    }
}
