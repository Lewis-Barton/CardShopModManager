using System.Reflection;
using CardShopModManager.Core;

namespace CardShopModManager.Cli;

/// <summary>Compare the running version against the latest GitHub release.</summary>
public static class UpdateCommand
{
    public static async Task Run()
    {
        var localVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        Console.WriteLine($"Local version: {localVersion}");

        var result = await new UpdateChecker("Lewis-Barton/CardShopModManager", localVersion)
            .CheckAsync(CancellationToken.None);

        if (result.Error is not null)
        {
            Console.WriteLine(result.Error);
            return;
        }

        if (!result.HasRelease)
        {
            Console.WriteLine("No GitHub releases published yet for this project.");
            return;
        }

        Console.WriteLine(result.IsUpToDate
            ? $"Up to date. Latest release: {result.LatestVersion}"
            : $"Update available: {result.LatestVersion} ({result.ReleaseUrl})");
    }
}