using CardShopModManager.Core;

namespace CardShopModManager.Cli;

public static class InstallCommand
{
    public static void Run(string? manifestPath, string? sourceDirectory, string? gameFolderPath)
    {
        if (manifestPath is null || sourceDirectory is null || gameFolderPath is null)
        {
            Console.WriteLine("Usage: install <manifest.json> <sourceDir> <gameFolder>");
            return;
        }

        var report = new DeploymentService().Install(manifestPath, sourceDirectory, gameFolderPath);

        foreach (var line in report.Lines)
            Console.WriteLine(line);

        if (!report.Success)
            Environment.ExitCode = 1;
    }
}