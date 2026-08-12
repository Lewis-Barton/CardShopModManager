using CardShopModManager.Core;

namespace CardShopModManager.Cli;

public static class DetectCommand
{
    public static void Run(string? folderPath)
    {
        // No path given: locate the game install through Steam automatically.
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            RunSteamDetection();
            return;
        }

        var detector = new GameDetector();
        var result = detector.Detect(folderPath);

        if (!result.IsValid)
        {
            Console.WriteLine($"Not a valid game folder: {result.Error}");
            Environment.ExitCode = 1;
            return;
        }

        Console.WriteLine($"Game found: {result.GameExecutablePath}");
    }

    private static void RunSteamDetection()
    {
        var steam = new SteamLocator();
        var installPath = steam.FindGameInstallPath(SteamLocator.GameAppId);

        if (installPath is null)
        {
            Console.WriteLine(
                "Could not find TCG Card Shop Simulator through Steam. " +
                "Run 'detect <gameFolder>' with the folder path manually.");
            Environment.ExitCode = 1;
            return;
        }

        // The Steam app manifest knows where the game is installed, so treat that
        // as the answer. A quick executable check is a soft confirmation only.
        var check = new GameDetector().Detect(installPath);
        if (check.IsValid)
        {
            Console.WriteLine($"Detected via Steam: {check.GameExecutablePath}");
            return;
        }

        Console.WriteLine(
            $"Detected TCG Card Shop Simulator via Steam at: {installPath}\n" +
            $"  (the expected executable '{SteamLocator.GameExecutableName}' was not found there — " +
            "check GameDetector.GameExecutableName / SteamLocator.GameExecutableName if the game renamed it)");
    }
}