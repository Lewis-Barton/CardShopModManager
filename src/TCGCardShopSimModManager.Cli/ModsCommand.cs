using TCGCardShopSimModManager.Core;

namespace TCGCardShopSimModManager.Cli;

/// <summary>
/// Inspect and toggle the mods actually present in a game folder. The list is
/// built from journal ownership plus unclaimed files under the BepInEx and
/// disabled roots; disabling moves a mod's files into manager-owned storage and
/// enabling moves them back — no destructive deletes.
/// </summary>
public static class ModsCommand
{
    public static void Run(string? operation, string? arg2, string? arg3)
    {
        switch (operation)
        {
            case "list":
                List(arg2);
                break;
            case "disable":
                Change(modName: arg2, gameFolder: arg3, disable: true);
                break;
            case "enable":
                Change(modName: arg2, gameFolder: arg3, disable: false);
                break;
            default:
                Console.WriteLine("Usage: mods <list <gameFolder> | disable <name> <gameFolder> | enable <name> <gameFolder>>");
                Environment.ExitCode = 2;
                break;
        }
    }

    private static void List(string? gameFolderPath)
    {
        if (gameFolderPath is null)
        {
            Console.WriteLine("Usage: mods list <gameFolder>");
            Environment.ExitCode = 2;
            return;
        }

        if (!Directory.Exists(gameFolderPath))
        {
            Console.WriteLine($"Game folder not found: {gameFolderPath}");
            Environment.ExitCode = 1;
            return;
        }

        var mods = ModDiscovery.Discover(gameFolderPath);
        if (mods.Count == 0)
        {
            Console.WriteLine("No mod folders found in this game folder.");
            return;
        }

        foreach (var mod in mods)
            Console.WriteLine($"  {mod.ModName,-40} {mod.State,-10} ({mod.FileCount} file(s))");
    }

    private static void Change(string? modName, string? gameFolder, bool disable)
    {
        if (modName is null || gameFolder is null)
        {
            Console.WriteLine(disable
                ? "Usage: mods disable <name> <gameFolder>"
                : "Usage: mods enable <name> <gameFolder>");
            Environment.ExitCode = 2;
            return;
        }

        var installer = new ModInstaller(gameFolder);
        if (disable)
        {
            var result = installer.Disable(modName);
            if (!result.Success)
            {
                Console.WriteLine(result.Error);
                Environment.ExitCode = 1;
                return;
            }
            Console.WriteLine($"Disabled {modName}.");
            if (result.Message is not null)
                Console.WriteLine($"  {result.Message}");
            foreach (var warning in result.Warnings)
                Console.WriteLine($"  Warning: {warning}");
        }
        else
        {
            var result = installer.Enable(modName);
            if (!result.Success)
            {
                Console.WriteLine(result.Error);
                Environment.ExitCode = 1;
                return;
            }
            Console.WriteLine($"Enabled {modName}.");
            if (result.Message is not null)
                Console.WriteLine($"  {result.Message}");
            foreach (var warning in result.Warnings)
                Console.WriteLine($"  Warning: {warning}");
        }
    }
}
