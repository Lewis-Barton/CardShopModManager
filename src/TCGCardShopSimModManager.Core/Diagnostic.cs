using System.Text.Json;

namespace TCGCardShopSimModManager.Core;

/// <summary>
/// Structured diagnostic logging. Writes one JSON line per event to a session
/// log under %LOCALAPPDATA%\TCGCardShopSimModManager\logs (override with CSMM_LOG_DIR).
/// Local-only debugging log written under %LOCALAPPDATA%.
/// </summary>
public static class Diagnostic
{
    private const int MaxTailBytes = 1024 * 1024;
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
            return ReadRecentLines(LogFilePath, max);
        }
    }

    internal static string[] ReadRecentLines(string path, int max)
    {
        if (max <= 0)
            return Array.Empty<string>();

        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            bufferSize: 4096, FileOptions.SequentialScan);

        var start = Math.Max(0, stream.Length - MaxTailBytes);
        stream.Seek(start, SeekOrigin.Begin);

        using var reader = new StreamReader(stream);
        if (start > 0)
            reader.ReadLine(); // discard the partial line at the start of the bounded window

        var lines = new Queue<string>(Math.Min(max, 500));
        while (reader.ReadLine() is { } line)
        {
            if (lines.Count == max)
                lines.Dequeue();
            lines.Enqueue(line);
        }

        return lines.ToArray();
    }

    private static string ResolvePath()
    {
        var configured = Environment.GetEnvironmentVariable("CSMM_LOG_DIR");
        var directory = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TCGCardShopSimModManager",
                "logs")
            : configured;

        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"session-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
    }
}
