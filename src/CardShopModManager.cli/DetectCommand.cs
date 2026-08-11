using CardShopModManager.Core;

namespace CardShopModManager.Cli;

public static class DetectCommand
{
    public static void Run(string? folderPath)
    {
        folderPath ??= Prompt("Enter the game folder path: ");

        var detector = new GameDetector();
        var result = detector.Detect(folderPath);

        if (!result.IsValid)
        {
            Console.WriteLine($"Not a valid game folder: {result.Error}");
            return;
        }

        Console.WriteLine($"Game found: {result.GameExecutablePath}");
    }

    private static string Prompt(string message)
    {
        Console.WriteLine(message);
        return Console.ReadLine() ?? string.Empty;
    }
}