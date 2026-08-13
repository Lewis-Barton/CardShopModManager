using TCGCardShopSimModManager.Core;

namespace TCGCardShopSimModManager.Cli;

public static class UninstallCommand
{
    public static void Run(string? modName, string? gameFolderPath)
    {
        if (modName is null || gameFolderPath is null)
        {
            Console.WriteLine("Usage: uninstall <modName> <gameFolder>");
            return;
        }

        // BUG-040: a missing game folder is distinct from "no journal entry".
        if (!Directory.Exists(gameFolderPath))
        {
            Console.WriteLine($"Game folder not found: {gameFolderPath}");
            Environment.ExitCode = 1;
            return;
        }

        var installer = new ModInstaller(gameFolderPath);
        var result = installer.Uninstall(modName);

        if (!result.Success)
        {
            Console.WriteLine(result.Error);
            Environment.ExitCode = 1;
            return;
        }

        Console.WriteLine($"Uninstalled {modName}.");
        foreach (var warning in result.Warnings)
            Console.WriteLine($"  Warning: {warning}");
    }
}