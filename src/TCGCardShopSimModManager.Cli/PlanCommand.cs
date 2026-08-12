using TCGCardShopSimModManager.Core;

namespace TCGCardShopSimModManager.Cli;

/// <summary>
/// The permanent dry-run: resolve every archive in the manifest into a
/// file-by-file plan without touching the game folder.
/// </summary>
public static class PlanCommand
{
    public static void Run(string? manifestPath, string? sourceDirectory, string? gameFolderPath)
    {
        if (manifestPath is null || sourceDirectory is null || gameFolderPath is null)
        {
            Console.WriteLine("Usage: plan <manifest.json> <sourceDir> <gameFolder>");
            return;
        }

        var manifest = new ManifestReader().Read(manifestPath);
        var validation = new ManifestValidator().Validate(manifest);

        if (!validation.IsValid)
        {
            Console.WriteLine("Manifest is invalid:");
            foreach (var error in validation.Errors)
                Console.WriteLine($"  - {error}");
            Environment.ExitCode = 1;
            return;
        }

        var installer = new ModInstaller(gameFolderPath);

        // Planning extracts each archive into a scratch folder to inspect it.
        // That scratch folder is thrown away after the plan is printed.
        var planRoot = Path.Combine(Path.GetTempPath(), "cardshopmodmanager-plan", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(planRoot);

        try
        {
            // Each archive gets its own extraction subfolder: plans are built from
            // what the archives contained, and two archives extracted into the same
            // folder would collide on files with the same name (e.g. README.md).
            var modNumber = 0;

            foreach (var mod in manifest.Mods)
            {
                modNumber++;
                Console.WriteLine($"\n[{mod.Archive}]");

                var extractionDir = Path.Combine(planRoot, $"mod-{modNumber}");

                try
                {
                    var plan = installer.CreatePlan(mod, sourceDirectory, extractionDir);

                    Console.WriteLine($"  layout: {plan.LayoutName}");
                    Console.WriteLine($"  {plan.Files.Count} file(s) to install, {plan.SkippedEntries.Count} skipped, {plan.RejectedEntries.Count} rejected");

                    foreach (var file in plan.Files)
                        Console.WriteLine($"    {file.SourceRelativePath}  ->  {file.DestinationRelativePath}");

                    foreach (var skip in plan.SkippedEntries)
                        Console.WriteLine($"    skip: {skip}");

                    foreach (var rejected in plan.RejectedEntries)
                        Console.WriteLine($"    rejected: {rejected}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  could not plan: {ex.Message}");
                    Environment.ExitCode = 1;
                }
            }
        }
        finally
        {
            if (Directory.Exists(planRoot))
                Directory.Delete(planRoot, recursive: true);
        }
    }
}