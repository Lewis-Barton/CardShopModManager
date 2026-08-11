using CardShopModManager.Core;
using CardShopModManager.Cli;

if (args.Length == 0)
{
    Console.WriteLine("Usage: cardshopmodmanager <detect|validate|plan|download|serve|demo|nexus|nexus-demo|install|uninstall|profile> [path]");
    return;
}

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