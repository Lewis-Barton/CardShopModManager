using System.IO;
using System.Text.Json;

namespace TCGCardShopSimModManager.Core;

/// <summary>
/// The Nexus OAuth client id (and optional redirect URI) for this install. The
/// client id is public, not a secret, so it is stored as plain JSON. Resolution
/// order in <see cref="NexusOAuth"/> is: environment variable, then this file,
/// then the built-in defaults.
/// </summary>
public sealed record OAuthSettings(string? ClientId = null, string? RedirectUri = null)
{
    private static string StoragePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TCGCardShopSimModManager",
            "oauth-settings.json");

    public static OAuthSettings Load()
    {
        if (!File.Exists(StoragePath))
            return new OAuthSettings();

        try
        {
            return JsonSerializer.Deserialize<OAuthSettings>(File.ReadAllText(StoragePath))
                ?? new OAuthSettings();
        }
        catch (JsonException)
        {
            return new OAuthSettings();
        }
    }

    public static void Save(OAuthSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StoragePath)!);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(StoragePath, json);
    }
}
