using System.Net.Http;
using TCGCardShopSimModManager.Core;

namespace TCGCardShopSimModManager.Cli;

public static class NexusCommand
{
    public static async Task Run(string? operation, string? arg1, string? arg2 = null)
    {
        switch (operation)
        {
            case "set-key":
                if (string.IsNullOrWhiteSpace(arg1))
                {
                    Console.WriteLine("Usage: nexus set-key <apikey>");
                    return;
                }

                ApiKeyStore.Save(arg1.Trim());
                Console.WriteLine("API key stored (DPAPI, readable only by the current user).");
                Console.WriteLine("The classic API-key path is for development only. Prefer 'nexus login' for real use.");
                break;

            case "set-client":
                if (string.IsNullOrWhiteSpace(arg1))
                {
                    Console.WriteLine("Usage: nexus set-client <clientId> [redirectUri]");
                    return;
                }

                OAuthSettings.Save(new OAuthSettings(
                    ClientId: arg1.Trim(),
                    RedirectUri: string.IsNullOrWhiteSpace(arg2) ? null : arg2.Trim()));
                Console.WriteLine($"OAuth client id set to {arg1.Trim()}.");
                if (!string.IsNullOrWhiteSpace(arg2))
                    Console.WriteLine($"Redirect URI set to {arg2.Trim()}.");
                Console.WriteLine("Subsequent 'nexus login' runs will use this client.");
                break;

            case "clear":
                ApiKeyStore.Delete();
                Console.WriteLine("Stored API key removed.");
                break;

            case "login":
                await LoginAsync();
                break;

            case "logout":
                NexusTokenStore.Delete();
                Console.WriteLine("Signed out of Nexus (OAuth tokens removed).");
                break;

            case "status":
                await ShowStatusAsync();
                break;

            default:
                Console.WriteLine("Usage: nexus <set-key <apikey>|set-client <id> [redirectUri]|login|logout|status|clear>");
                Console.WriteLine("  set-client <id>  store the Nexus OAuth client id (production path)");
                Console.WriteLine("  login            authenticate with Nexus via OAuth (preferred)");
                Console.WriteLine("  set-key <apikey> store a classic API key (development only)");
                Console.WriteLine("  API base comes from NEXUS_API_BASE, defaulting to https://api.nexusmods.com/v1");
                break;
        }
    }

    private static async Task LoginAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var user = await NexusOAuth.LoginAsync(http, Console.WriteLine);

            Console.WriteLine();
            Console.WriteLine($"Signed in as {user.Name} (id {user.UserId})");
            Console.WriteLine(user.IsPremium
                ? "Account: PREMIUM (automatic downloads allowed)"
                : "Account: free — downloads must go through the manual/browser flow");
        }
        catch (DownloadException ex)
        {
            Console.WriteLine($"Login failed: {ex.Message}");
        }
    }

    private static async Task ShowStatusAsync()
    {
        var baseUrl = ApiBaseUrl();

        // OAuth takes precedence when a token is stored.
        if (NexusTokenStore.Exists && NexusTokenStore.TryLoad() is { } set)
        {
            var user = NexusJwt.DecodeAccessToken(set.AccessToken);
            Console.WriteLine($"API base: {baseUrl}");
            Console.WriteLine($"Auth: OAuth (Bearer token, expires {set.ExpiresAt:u})");
            if (user is null)
            {
                Console.WriteLine("Signed in, but the stored token could not be read — run 'nexus login' again.");
            }
            else
            {
                Console.WriteLine($"User: {user.Name} (id {user.UserId})");
                Console.WriteLine(user.IsPremium
                    ? "Account: PREMIUM (automatic downloads allowed)"
                    : "Account: free — downloads must go through the manual/browser flow");
            }

            return;
        }

        var apiKey = ApiKeyStore.TryLoad();
        if (apiKey is null)
        {
            Console.WriteLine($"API base: {baseUrl}");
            Console.WriteLine("No Nexus credentials stored — run 'nexus login' (OAuth) or 'nexus set-key <apikey>' (dev).");
            return;
        }

        Console.WriteLine($"API base: {baseUrl}");
        Console.WriteLine("Auth: classic API key (development only).");

        try
        {
            var api = new NexusApi(baseUrl, NexusApi.GameDomain, NexusApi.UserAgent);
            var user = await api.GetUserAsync(NexusAuth.FromApiKey(apiKey), CancellationToken.None);

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

    internal static string ApiBaseUrl() => NexusApi.ApiBaseUrl();
}
