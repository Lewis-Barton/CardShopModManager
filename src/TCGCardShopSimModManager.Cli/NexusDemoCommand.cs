using TCGCardShopSimModManager.Core;

namespace TCGCardShopSimModManager.Cli;

/// <summary>
/// One-command demo of the Nexus path: serves a mock of the Nexus v1 API over
/// the archives folder (premium user), downloads every manifest mod through
/// <see cref="NexusModSource"/>, then installs into a game folder. Single
/// terminal, automatic server stop.
/// </summary>
public static class NexusDemoCommand
{
    public static async Task Run(
        string? manifestPath,
        string? archiveFolder,
        string? cacheDir,
        string? outDir,
        string? gameFolder)
    {
        manifestPath ??= "samples/manifests/nexus-demo.json";
        archiveFolder ??= "samples/mod-archives";
        cacheDir ??= Path.Combine(Path.GetTempPath(), "cardshop-demo-cache");
        outDir ??= Path.Combine(Path.GetTempPath(), "cardshop-demo-out");
        gameFolder ??= Path.Combine(Path.GetTempPath(), "cardshop-demo-game");

        if (!File.Exists(manifestPath) || !Directory.Exists(archiveFolder))
        {
            Console.WriteLine("Usage: nexus-demo [manifest.json] [archivesFolder] [cacheDir] [outDir] [gameFolder]");
            Console.WriteLine("  (defaults use samples/manifests/nexus-demo.json + samples/mod-archives from the repo root)");
            return;
        }

        Directory.CreateDirectory(cacheDir);
        Directory.CreateDirectory(outDir);
        Directory.CreateDirectory(gameFolder);

        var manifest = new ManifestReader().Read(manifestPath);
        var validation = new ManifestValidator().Validate(manifest);
        if (!validation.IsValid)
        {
            Console.WriteLine("Manifest is invalid:");
            foreach (var error in validation.Errors)
                Console.WriteLine($"  - {error}");
            return;
        }

        // The mock assigns one mod id and one file id per archive in the manifest.
        var files = manifest.Mods
            .Select((m, i) => (ModId: 4000L + i, FileId: 7000L + i, m.Archive))
            .ToList();

        // The mock needs the server's own URL (the port only exists after this line).
using var server = new LocalHttpServer();
server.Provider = NexusMock.MakeProvider(
    archiveFolder,
    manifest.Game,
    server.Url(""),
    files,
    premium: true);

        var apiBase = server.Url("v1");
        Console.WriteLine($"Mock Nexus API at {apiBase}/");
        Console.WriteLine("  (premium user — automatic download flow)\n");

        var downloader = new ModDownloader(
            new NexusModSource(apiBase, manifest.Game, NexusAuth.FromApiKey("demo-key")),
            new DownloadOptions { CacheDirectory = cacheDir });

        foreach (var entry in manifest.Mods)
        {
            var mod = new ModReference(entry.Id, entry.Archive, entry.Sha256, entry.Version,
                entry.NexusModId, entry.NexusFileId);

            Console.WriteLine($"Downloading {entry.Name} ({entry.Archive})...");
            var result = await downloader.DownloadAsync(mod, outDir);

            Console.WriteLine(result.Success
                ? $"  OK{(result.FromCache ? " (from cache)" : "")} -> {result.DestinationPath}"
                : $"  FAILED: {result.Error}");
        }

        Console.WriteLine("\nInstalling into the game folder...");
        var report = new DeploymentService().Install(manifestPath, outDir, gameFolder);
        foreach (var line in report.Lines)
            Console.WriteLine($"  {line}");

        Console.WriteLine($"\nDone. Game folder: {gameFolder}");
    }
}