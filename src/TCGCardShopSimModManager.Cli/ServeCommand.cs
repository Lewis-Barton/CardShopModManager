using TCGCardShopSimModManager.Core;

namespace TCGCardShopSimModManager.Cli;

/// <summary>
/// Hosts a folder over HTTP with Range support, using the same in-process
/// server the tests use. Exists so the download pipeline can be tried end-to-end
/// offline. Press Enter to stop.
/// </summary>
public static class ServeCommand
{
    public static void Run(string? folderPath, string? portArg)
    {
        if (folderPath is null || !Directory.Exists(folderPath))
        {
            Console.WriteLine("Usage: serve <folder> [port]");
            return;
        }

        var port = int.TryParse(portArg, out var parsed) ? parsed : (int?)null;
        var server = new LocalHttpServer(port) { Provider = LocalHttpServer.FolderProvider(folderPath) };

        Console.WriteLine($"Serving {folderPath}");
        Console.WriteLine($"  http://localhost:{server.Port}/");
        Console.WriteLine();
        Console.WriteLine("This command IS the server: it stays open so other");
        Console.WriteLine("terminals can download from it.");
        Console.WriteLine("  - Run downloads from a SECOND terminal,");
        Console.WriteLine("  - or use the single-terminal 'demo' command instead.");
        Console.WriteLine();
        Console.WriteLine("Press Ctrl+C to stop.");

        // Block until Ctrl+C (works interactively) or the process is killed
        // (works headless). Waiting on stdin via ReadLine would exit immediately
        // when stdin is closed, e.g. when launched from another tool.
        var stop = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Set();
        };
        stop.Wait();

        server.Dispose();
    }
}