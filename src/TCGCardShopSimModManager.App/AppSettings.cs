using System;
using System.IO;
using System.Text.Json;

namespace TCGCardShopSimModManager.App;

/// <summary>
/// App-level UI preferences that persist between sessions (separate from the
/// engine's credential/settings stores). Today it holds the "confirmed 18+"
/// flag that gates NSFW mod lists; more preferences can be added as fields.
/// </summary>
public sealed record AppSettings(bool Confirmed18Plus = false)
{
    private static string StoragePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TCGCardShopSimModManager",
            "app-settings.json");

    public static AppSettings Load()
    {
        if (!File.Exists(StoragePath))
            return new AppSettings();

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(StoragePath))
                ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StoragePath)!);
        var temporaryPath = StoragePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath,
                JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, StoragePath, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporaryPath); } catch { /* best effort */ }
        }
    }
}
