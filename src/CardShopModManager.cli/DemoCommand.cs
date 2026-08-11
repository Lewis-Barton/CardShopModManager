using CardShopModManager.Core;

namespace CardShopModManager.Cli;

/// <summary>
/// One-command demo: start an in-process server over the archives folder,
/// download every mod in the manifest from it through the full download
/// pipeline, then install into a game folder. Everything runs in a single
/// terminal — the server is started and stopped automatically.
///
/// With no arguments it uses the repo's sample manifest and archives, so
/// "dotnet run --project src/CardShopModManager.Cli -- demo" works from the
/// project root.
/// </summary>
public static class DemoCommand
{
    public static async Task Run(
        string? manifestPath,
        string? archiveFolder,
        string? cacheDir,
        string? outDir,
        string? gameFolder)
    {
        manifestPath ??= "samples/manifests/archive-demo.json";
        archiveFolder ??= "samples/mod-archives";
        cacheDir ??= Path.Combine(Path.GetTempPath(), "cardshop-demo-cache");
        outDir ??= Path.Combine(Path.GetTempPath(), "cardshop-demo-out");
        gameFolder ??= Path.Combine(Path.GetTempPath(), "cardshop-demo-game");

        if (!File.Exists(manifestPath) || !Directory.Exists(archiveFolder))
        {
            Console.WriteLine("Usage: demo [manifest.json] [archivesFolder] [cacheDir] [outDir] [gameFolder]");
            Console.WriteLine("  (defaults use samples/manifests/archive-demo.json + samples/mod-archives from the repo root)");
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

        // Start the in-process server, serve the archives folder, and stop it
        // automatically when the demo finishes (using ... disposes the server).
        using var server = new LocalHttpServer { Provider = LocalHttpServer.FolderProvider(archiveFolder) };
        var baseUrl = $"http://localhost:{server.Port}";
        Console.WriteLine($"Serving {archiveFolder}");
        Console.WriteLine($"  {baseUrl}/\n");

        var downloader = new ModDownloader(
            new HttpModSource(mod => $"{baseUrl}/{Uri.EscapeDataString(mod.FileName)}"),
            new DownloadOptions { CacheDirectory = cacheDir });

        foreach (var entry in manifest.Mods)
        {
            var mod = new ModReference(entry.Id, entry.Archive, entry.Sha256, entry.Version);
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
        Console.WriteLine("Run the demo again to see the download cache skip the network.");
    }
}