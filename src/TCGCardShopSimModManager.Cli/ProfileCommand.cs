using TCGCardShopSimModManager.Core;

namespace TCGCardShopSimModManager.Cli;

/// <summary>
/// Named profiles: which mod ids are enabled for this game install. Enabling
/// installs the mod and its (enabled) dependencies in order; disabling removes
/// the mod's files from the active game directory. The profile change is only
/// committed after the prospective state is proven valid.
/// </summary>
public static class ProfileCommand
{
    public static void Run(
        string? operation,
        string? arg2,
        string? arg3,
        string? arg4,
        string? arg5)
    {
        switch (operation)
        {
            case "list":
                ListProfiles(arg2);
                break;
            case "use":
                UseProfile(arg2, arg3);
                break;
            case "enable":
                EnableMod(arg2, arg3, arg4, arg5);
                break;
            case "disable":
                DisableMod(arg2, arg3, arg4, arg5);
                break;
            default:
                Console.WriteLine(
                    "Usage: profile <list|use|enable|disable> ...\n" +
                    "  profile list <gameFolder>\n" +
                    "  profile use <name> <gameFolder>\n" +
                    "  profile enable <id> <manifest.json> <sourceDir> <gameFolder>\n" +
                    "  profile disable <id> <manifest.json> <sourceDir> <gameFolder>");
                Environment.ExitCode = 2;
                break;
        }
    }

    private static void ListProfiles(string? gameFolderPath)
    {
        if (gameFolderPath is null)
        {
            Console.WriteLine("Usage: profile list <gameFolder>");
            Environment.ExitCode = 2;
            return;
        }

        var store = new ProfilesStore(gameFolderPath);
        if (!store.Exists)
        {
            Console.WriteLine($"No profile file at {Path.Combine(gameFolderPath, "cardshopmodmanager.profiles.json")}");
            Console.WriteLine("Every mod in the manifest is enabled by default.");
            return;
        }

        var state = store.Load();
        Console.WriteLine($"Profile file: {store.FilePath}");
        Console.WriteLine($"Active profile: {state.ActiveProfile}");
        foreach (var (name, ids) in state.Profiles)
            Console.WriteLine($"  {name} ({ids.Count}): {string.Join(", ", ids)}");
    }

    private static void UseProfile(string? name, string? gameFolderPath)
    {
        if (name is null || gameFolderPath is null)
        {
            Console.WriteLine("Usage: profile use <name> <gameFolder>");
            Environment.ExitCode = 2;
            return;
        }

        var store = new ProfilesStore(gameFolderPath);
        if (!store.Use(name))
        {
            Console.WriteLine($"No profile named '{name}'.");
            Environment.ExitCode = 1;
            return;
        }

        Console.WriteLine($"Active profile is now '{name}'.");
    }

    private static void EnableMod(
        string? modId,
        string? manifestPath,
        string? sourceDirectory,
        string? gameFolderPath)
    {
        if (modId is null || manifestPath is null || sourceDirectory is null || gameFolderPath is null)
        {
            Console.WriteLine("Usage: profile enable <id> <manifest.json> <sourceDir> <gameFolder>");
            Environment.ExitCode = 2;
            return;
        }

        Print(new ProfileService(gameFolderPath).Enable(
            new ManifestReader().Read(manifestPath), modId, sourceDirectory));
    }

    private static void DisableMod(
        string? modId,
        string? manifestPath,
        string? sourceDirectory,
        string? gameFolderPath)
    {
        if (modId is null || manifestPath is null || sourceDirectory is null || gameFolderPath is null)
        {
            Console.WriteLine("Usage: profile disable <id> <manifest.json> <sourceDir> <gameFolder>");
            Environment.ExitCode = 2;
            return;
        }

        Print(new ProfileService(gameFolderPath).Disable(
            new ManifestReader().Read(manifestPath), modId, sourceDirectory));
    }

    private static void Print(ProfileChangeResult result)
    {
        foreach (var message in result.Messages)
            Console.WriteLine(message);
        if (result.Error is not null)
            Console.WriteLine(result.Error);
        foreach (var warning in result.Warnings)
            Console.WriteLine($"Warning: {warning}");
        if (!result.Success)
            Environment.ExitCode = 1;
    }
}
