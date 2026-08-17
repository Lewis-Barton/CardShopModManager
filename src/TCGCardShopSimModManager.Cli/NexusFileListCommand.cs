using TCGCardShopSimModManager.Core;

namespace TCGCardShopSimModManager.Cli;

public static class NexusFileListCommand
{
    public static async Task Run(string? modValue)
    {
        if (string.IsNullOrWhiteSpace(modValue) || !NexusModLink.TryParse(modValue, out var link) || link is null)
        {
            Console.WriteLine("Usage: modpack files <Nexus Files-tab URL | modId>");
            Environment.ExitCode = 2;
            return;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var auth = NexusAuth.Unified(http);
        using var api = new NexusApi(NexusApi.ApiBaseUrl(), NexusApi.GameDomain, NexusApi.UserAgent, http);
        try
        {
            var mod = await api.GetModInfoAsync(link.ModId, auth, CancellationToken.None);
            var files = await api.ListFilesAsync(link.ModId, auth, CancellationToken.None);
            Console.WriteLine($"{mod.Name} — Nexus mod {link.ModId}");
            if (files.Count == 0)
            {
                Console.WriteLine("No downloadable files were returned by Nexus.");
                return;
            }

            foreach (var file in files
                         .OrderBy(file => CategoryOrder(file.Category))
                         .ThenBy(file => file.DisplayName ?? file.FileName, StringComparer.OrdinalIgnoreCase))
            {
                var category = string.IsNullOrWhiteSpace(file.Category) ? "UNCATEGORIZED" : file.Category;
                var version = string.IsNullOrWhiteSpace(file.Version) ? "version unknown" : $"v{file.Version}";
                var size = file.SizeBytes is { } bytes ? $", {FormatSize(bytes)}" : string.Empty;
                Console.WriteLine();
                Console.WriteLine($"[{category}] {file.DisplayName ?? file.FileName}");
                Console.WriteLine($"  {file.FileName} — {version}{size}");
                Console.WriteLine($"  required nexus:{link.ModId}:{file.FileId}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not list Nexus files: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    private static int CategoryOrder(string? category) => category?.ToUpperInvariant() switch
    {
        "MAIN" => 0,
        "UPDATE" => 1,
        "OPTIONAL" => 2,
        "MISCELLANEOUS" => 3,
        "OLD_VERSION" or "ARCHIVED" => 5,
        _ => 4
    };

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }
}
