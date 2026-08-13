using TCGCardShopSimModManager.Core;

namespace TCGCardShopSimModManager.Cli;

public static class ValidateCommand
{
    public static void Run(string? manifestPath, string? gameFolderPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            Console.WriteLine("Usage: validate <manifest.json> [gameFolder]");
            Environment.ExitCode = 2;
            return;
        }

        var report = new DeploymentService().Validate(manifestPath, gameFolderPath);

        foreach (var line in report.Lines)
            Console.WriteLine(line);

        if (!report.Success)
            Environment.ExitCode = 1;
    }
}