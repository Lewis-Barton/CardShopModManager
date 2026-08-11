using CardShopModManager.Core;

namespace CardShopModManager.Cli;

public static class NexusCommand
{
    public static async Task Run(string? operation, string? apiKeyOrNothing)
    {
        switch (operation)
        {
            case "set-key":
                if (string.IsNullOrWhiteSpace(apiKeyOrNothing))
                {
                    Console.WriteLine("Usage: nexus set-key <apikey>");
                    return;
                }

                ApiKeyStore.Save(apiKeyOrNothing.Trim());
                Console.WriteLine("API key stored (DPAPI, readable only by the current user).");
                break;

            case "clear":
                ApiKeyStore.Delete();
                Console.WriteLine("Stored API key removed.");
                break;

            case "status":
                await ShowStatusAsync();
                break;

            default:
                Console.WriteLine("Usage: nexus <set-key <apikey>|status|clear>");
                Console.WriteLine("  API base comes from NEXUS_API_BASE, defaulting to https://api.nexusmods.com/v1");
                break;
        }
    }

    private static async Task ShowStatusAsync()
    {
        var baseUrl = ApiBaseUrl();
        var apiKey = ApiKeyStore.TryLoad();

        if (apiKey is null)
        {
            Console.WriteLine("No API key stored — run 'nexus set-key <apikey>' first.");
            Console.WriteLine($"API base: {baseUrl}");
            return;
        }

        Console.WriteLine($"API base: {baseUrl}");

        try
        {
            var api = new NexusApi(baseUrl, "tcgcardshopsimulator", "CardShopModManager/dev");
            var user = await api.GetUserAsync(apiKey, CancellationToken.None);

            Console.WriteLine($"User: {user.Name} (id {user.UserId})");
            Console.WriteLine(user.IsPremium
                ? "Account: PREMIUM (automatic downloads allowed)"
                : "Account: free — downloads must go through the manual/browser flow");
        }
        catch (DownloadException ex)
        {
            Console.WriteLine($"Status check failed: {ex.Message}");
        }
    }

    internal static string ApiBaseUrl() =>
        Environment.GetEnvironmentVariable("NEXUS_API_BASE") ?? "https://api.nexusmods.com/v1";
}