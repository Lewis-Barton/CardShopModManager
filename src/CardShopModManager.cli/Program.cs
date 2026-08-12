using CardShopModManager.Core;
using CardShopModManager.Cli;

if (args.Length > 0 && args[0] is "--version" or "-v")
{
    var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
    Console.WriteLine($"CardShopModManager.Cli {version}");
    return;
}

if (args.Length == 0)
{
    Console.WriteLine("Usage: cardshopmodmanager <detect|validate|plan|download|serve|demo|nexus|nexus-demo|update-check|support-bundle|install|uninstall|profile> [path]");
    return;
}

// The key (and anything like it) must never appear in the diagnostic log.
Diagnostic.Write($"command: {RedactArgs(args)}");

try
{
    switch (args[0])
    {
        case "detect":
            DetectCommand.Run(args.ElementAtOrDefault(1));
            break;
        case "validate":
            ValidateCommand.Run(args.ElementAtOrDefault(1), args.ElementAtOrDefault(2));
            break;
        case "plan":
            PlanCommand.Run(args.ElementAtOrDefault(1), args.ElementAtOrDefault(2), args.ElementAtOrDefault(3));
            break;
        case "download":
            await DownloadCommand.Run(
                args.ElementAtOrDefault(1),
                args.ElementAtOrDefault(2),
                args.ElementAtOrDefault(3),
                args.ElementAtOrDefault(4));
            break;
        case "serve":
            ServeCommand.Run(args.ElementAtOrDefault(1), args.ElementAtOrDefault(2));
            break;
        case "demo":
            await DemoCommand.Run(
                args.ElementAtOrDefault(1),
                args.ElementAtOrDefault(2),
                args.ElementAtOrDefault(3),
                args.ElementAtOrDefault(4),
                args.ElementAtOrDefault(5));
            break;
        case "nexus":
            await NexusCommand.Run(args.ElementAtOrDefault(1), args.ElementAtOrDefault(2));
            break;
        case "nexus-demo":
            await NexusDemoCommand.Run(
                args.ElementAtOrDefault(1),
                args.ElementAtOrDefault(2),
                args.ElementAtOrDefault(3),
                args.ElementAtOrDefault(4),
                args.ElementAtOrDefault(5));
            break;
        case "update-check":
            await UpdateCommand.Run();
            break;
        case "support-bundle":
            SupportCommand.Run(args.ElementAtOrDefault(1));
            break;
        case "install":
            InstallCommand.Run(args.ElementAtOrDefault(1), args.ElementAtOrDefault(2), args.ElementAtOrDefault(3));
            break;
        case "uninstall":
            UninstallCommand.Run(args.ElementAtOrDefault(1), args.ElementAtOrDefault(2));
            break;
        case "profile":
            ProfileCommand.Run(
                args.ElementAtOrDefault(1),
                args.ElementAtOrDefault(2),
                args.ElementAtOrDefault(3),
                args.ElementAtOrDefault(4),
                args.ElementAtOrDefault(5));
            break;
        default:
            Console.WriteLine($"Unknown command: {args[0]}");
            break;
    }

    Diagnostic.Write($"command completed: {args[0]}");
}
catch (Exception ex)
{
    // Local-only crash capture. Nothing is uploaded anywhere, ever.
    Diagnostic.Write($"unhandled exception: {ex.Message}", "error");
    Console.Error.WriteLine($"Unexpected error: {ex.Message}");
    Console.Error.WriteLine("Details were written to the diagnostic log. Export it with: support-bundle");
    Environment.ExitCode = 1;
}

static string RedactArgs(string[] args)
{
    var list = args.ToList();
    for (var i = 0; i < list.Count - 1; i++)
    {
        if (list[i].Equals("set-key", StringComparison.OrdinalIgnoreCase))
            list[i + 1] = "***";
    }

    return string.Join(' ', list);
}