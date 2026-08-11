using CardShopModManager.Core;

namespace CardShopModManager.Cli;

/// <summary>
/// Download every archive in the manifest into a local out-folder, through the
/// full safety pipeline (partial files, resume, hash verify, retries, cache).
/// The source is either an http(s) base URL or a local folder.
/// </summary>
public static class DownloadCommand
{
    public static async Task Run(string? manifestPath, string? sourceSpec, string? cacheDir, string? outDir)
    {
        if (manifestPath is null || sourceSpec is null || cacheDir is null || outDir is null)
        {
            Console.WriteLine("Usage: download <manifest.json> <httpUrlBase|localFolder> <cacheDir> <outDir>");
            return;
        }

        var manifest = new ManifestReader().Read(manifestPath);
        var validation = new ManifestValidator().Validate(manifest);
        if (!validation.IsValid)
        {
            Console.WriteLine("Manifest is invalid:");
            foreach (var error in validation.Errors)
                Console.WriteLine($"  - {error}");
            return;
        }

        var source = CreateSource(sourceSpec, manifest.Game);
        var downloader = new ModDownloader(source, new DownloadOptions { CacheDirectory = cacheDir });

        foreach (var entry in manifest.Mods)
        {
            var mod = new ModReference(entry.Id, entry.Archive, entry.Sha256, entry.Version,
                entry.NexusModId, entry.NexusFileId);
            Console.WriteLine($"\n{entry.Name} ({entry.Archive})");

            var lastPercentage = -1;
            var result = await downloader.DownloadAsync(mod, outDir, progress =>
            {
                var percentage = progress.TotalBytes is long total && total > 0
                    ? (int)(100 * progress.DownloadedBytes / total)
                    : -1;
                if (percentage != lastPercentage && percentage >= 0)
                {
                    lastPercentage = percentage;
                    Console.Write($"\r  {percentage,3}%");
                }
            });

            if (lastPercentage >= 0)
                Console.WriteLine();

            if (result.Success)
            {
                Console.WriteLine($"  OK{(result.FromCache ? " (from cache)" : "")} -> {result.DestinationPath}");
            }
            else
            {
                Console.WriteLine($"  FAILED: {result.Error}");
            }
        }
    }

    private static IModSource CreateSource(string sourceSpec, string gameDomain)
    {
        if (sourceSpec.Equals("nexus", StringComparison.OrdinalIgnoreCase))
        {
            return new NexusModSource(
                NexusCommand.ApiBaseUrl(),
                gameDomain,
                ApiKeyStore.TryLoad,
                userAgent: "CardShopModManager/dev");
        }

        if (sourceSpec.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            sourceSpec.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var baseUrl = sourceSpec.TrimEnd('/');
            return new HttpModSource(mod => $"{baseUrl}/{Uri.EscapeDataString(mod.FileName)}");
        }

        return new LocalFileSource(sourceSpec);
    }
}