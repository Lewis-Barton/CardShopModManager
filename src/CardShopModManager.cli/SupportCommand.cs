using CardShopModManager.Core;

namespace CardShopModManager.Cli;

/// <summary>Export a support bundle (logs + environment info) for sharing.</summary>
public static class SupportCommand
{
    public static void Run(string? outputDirectory)
    {
        var path = SupportBundle.Create(gameFolder: null, outputDirectory);
        Console.WriteLine($"Support bundle written to: {path}");
    }
}