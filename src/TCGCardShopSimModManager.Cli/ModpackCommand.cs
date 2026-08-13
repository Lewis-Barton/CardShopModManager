using System.Net.Http;
using TCGCardShopSimModManager.Core;

namespace TCGCardShopSimModManager.Cli;

/// <summary>
/// Install a modpack hosted on GitHub: fetch the index, download the pack's
/// archives, then run the standard install pipeline.
///
///   modpack list                       show the available packs
///   modpack install &lt;packId&gt; [game]    download + install the pack
/// </summary>
public static class ModpackCommand
{
    public static async Task Run(string? sub, string? arg1, string? arg2)
    {
        // `validate` is a local authoring check against modpacks/ on disk — it
        // never touches GitHub, so handle it before the live-index path.
        if (sub is "validate")
        {
            var root = arg2 ?? "modpacks";
            var validator = new ModpackSubmissionValidator(root);

            if (arg1 is null)
            {
                var all = validator.ValidateAll();
                var ok = true;
                foreach (var (id, result) in all)
                {
                    PrintSubmission(id, result);
                    ok &= result.IsValid;
                }
                Console.WriteLine(ok ? "All packs valid." : "Some packs failed validation.");
                // BUG-031: a failed validation must not exit 0.
                Environment.ExitCode = ok ? 0 : 1;
                return;
            }

            PrintSubmission(arg1, validator.ValidatePack(arg1));
            return;
        }

        var reader = new ModpackIndexReader();
        var index = await reader.FetchIndexAsync();

        if (sub is not "install")
        {
            if (index.Packs.Count == 0)
            {
                Console.WriteLine("No modpacks are published yet.");
                return;
            }

            Console.WriteLine("Available modpacks:");
            foreach (var p in index.Packs)
                Console.WriteLine($"  {p.Id,-22} {p.Name} — {p.ShortDescription}");
            return;
        }

        var packId = arg1 ?? throw new ArgumentException("modpack install needs a pack id");
        var summary = index.Packs.FirstOrDefault(p =>
            p.Id.Equals(packId, StringComparison.OrdinalIgnoreCase));

        if (summary is null)
        {
            Console.WriteLine($"No pack named '{packId}'. Run 'modpack list' to see available packs.");
            return;
        }

        var gameFolder = arg2 ?? new SteamLocator().FindGameInstallPath(SteamLocator.GameAppId);
        if (gameFolder is null)
        {
            Console.WriteLine("Could not auto-detect the game folder. Pass it as the last argument.");
            return;
        }

        var manifest = await reader.FetchManifestAsync(summary);

        IModSource? fallback = summary.Source is null
            ? null
            : summary.Source.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? new HttpModSource(m => $"{summary.Source.TrimEnd('/')}/{Uri.EscapeDataString(m.FileName)}")
                : new LocalFileSource(summary.Source);

        Console.WriteLine($"Installing {summary.Name} into {gameFolder}...");
        var report = await new ModpackInstaller(gameFolder).InstallAsync(manifest, fallback, pack: summary);

        foreach (var line in report.Lines)
            Console.WriteLine(line);
    }

    private static void PrintSubmission(string packId, SubmissionResult result)
    {
        var tag = result.IsValid ? "VALID" : "INVALID";
        Console.WriteLine($"[{tag}] {packId}");
        foreach (var error in result.Errors)
            Console.WriteLine($"  error: {error}");
        foreach (var warning in result.Warnings)
            Console.WriteLine($"  warning: {warning}");
    }
}
