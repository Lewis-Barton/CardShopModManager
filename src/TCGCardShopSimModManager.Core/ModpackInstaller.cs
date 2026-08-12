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

        // Pre-flight: if the pack declares a total download size, refuse early
        // (before touching the network) when the download temp location or the
        // game folder lacks room. The per-file gate in ModDownloader is a
        // backstop for any mod whose real size exceeds the declared total.
        if (manifest.TotalSize is { } total && total > 0)
        {
            var margin = 25L * 1024 * 1024; // 25 MiB headroom for extraction overhead
            if (!HasFreeSpace(cacheDirectory, total + margin, out var downloadMsg))
                return DeploymentReport.Failure(new List<string>(), downloadMsg);
            if (!HasFreeSpace(_gameFolderPath, total + margin, out var installMsg))
                return DeploymentReport.Failure(new List<string>(), installMsg);
        }

        // The fallback only matters for mods with neither a DownloadUrl nor a Nexus id; point it at the cache so an already-downloaded file is reused.
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

        var report = new DeploymentService().Install(manifest, cacheDirectory, _gameFolderPath);

        // The install copied what it needed out of the temp cache; drop it so a
        // finished modpack doesn't leave the downloaded archives on disk.
        if (report.Success)
            TryDeleteDirectory(cacheDirectory);

        return report;
    }

    private static bool HasFreeSpace(string path, long neededBytes, out string message)
    {
        message = string.Empty;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path)) ?? string.Empty;
            var free = new DriveInfo(root).AvailableFreeSpace;
            if (free < neededBytes)
            {
                message = $"Not enough free disk space on '{root}': need {neededBytes} bytes, only {free} free.";
                return false;
            }
            return true;
        }
        catch
        {
            // Can't read free space (network drive, unusual root) — don't block
            // on a false alarm; let a real write failure surface later.
            return true;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; a leftover temp folder is harmless.
        }
    }
}
