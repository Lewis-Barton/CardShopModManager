using CardShopModManager.Core;

namespace CardShopModManager.Cli;

public static class UninstallCommand
{
    public static void Run(string? modName, string? gameFolderPath)
    {
        if (modName is null || gameFolderPath is null)
        {
            Console.WriteLine("Usage: uninstall <modName> <gameFolder>");
            return;
        }

        var installer = new ModInstaller(gameFolderPath);
        var result = installer.Uninstall(modName);

        if (!result.Success)
        {
            Console.WriteLine(result.Error);
            return;
        }

        Console.WriteLine($"Uninstalled {modName}.");
        foreach (var warning in result.Warnings)
            Console.WriteLine($"  Warning: {warning}");
    }
}