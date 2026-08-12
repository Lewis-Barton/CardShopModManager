using System.Text.Json;

namespace CardShopModManager.Core;

/// <summary>
/// Structured diagnostic logging. Writes one JSON line per event to a session
/// log under %LOCALAPPDATA%\CardShopModManager\logs (override with CSMM_LOG_DIR).
/// Local only — nothing is ever sent anywhere, by design.
/// </summary>
public static class Diagnostic
{
    private static readonly object Gate = new();
    private static string? _filePath;

    public static string LogFilePath => _filePath ??= ResolvePath();

    public static void Write(string message, string category = "app")
    {
        lock (Gate)
        {
            var line = JsonSerializer.Serialize(new
            {
                ts = DateTimeOffset.Now.ToString("o"),
                category,
                message
            });

            File.AppendAllText(LogFilePath, line + Environment.NewLine);
        }
    }

    public static string[] RecentLines(int max = 500)
    {
        lock (Gate)
        {
            if (!File.Exists(LogFilePath))
                return Array.Empty<string>();
            return File.ReadAllLines(LogFilePath).TakeLast(max).ToArray();
        }
    }

    private static string ResolvePath()
    {
        var configured = Environment.GetEnvironmentVariable("CSMM_LOG_DIR");
        var directory = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CardShopModManager",
                "logs")
            : configured;

        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"session-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
    }
}