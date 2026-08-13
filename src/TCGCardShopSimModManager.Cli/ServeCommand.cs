using System;
using System.Threading;
using System.Threading.Tasks;
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

        // Block until a shutdown signal arrives. BUG-039: a headless/automated
        // run may have no attached console, so the Ctrl+C handler alone is not
        // enough — also watch for stdin EOF and process exit so the server is
        // disposed cleanly in every case.
        var stop = new ManualResetEventSlim(false);

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Set();
        };

        // A closed stdin (e.g. launched with stdin from /dev/null) returns null
        // from the first read; an open one just blocks until the user presses
        // Enter, which is a fine way to stop too.
        var stdinWatcher = Task.Run(async () =>
        {
            try
            {
                await Console.In.ReadLineAsync();
            }
            catch
            {
                // stdin unavailable — fall through and signal stop below.
            }
            stop.Set();
        });

        // BUG-039: release the listener even when the process is terminated by a
        // signal (e.g. SIGINT/SIGTERM) we don't observe directly. Double Dispose
        // is guarded inside LocalHttpServer.
        void OnProcessExit(object? _, EventArgs __) => server.Dispose();
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        stop.Wait();

        server.Dispose();
        try { stdinWatcher.Wait(TimeSpan.FromMilliseconds(100)); } catch { }
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
    }
}