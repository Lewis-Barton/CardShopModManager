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
                break;
        }
    }

    private static void ListProfiles(string? gameFolderPath)
    {
        if (gameFolderPath is null)
        {
            Console.WriteLine("Usage: profile list <gameFolder>");
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
            return;
        }

        var store = new ProfilesStore(gameFolderPath);
        if (!store.Use(name))
        {
            Console.WriteLine($"No profile named '{name}'.");
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
            return;
        }

        var manifest = new ManifestReader().Read(manifestPath);
        var mod = FindMod(manifest, modId);
        if (mod is null)
            return;

        var store = new ProfilesStore(gameFolderPath);
        if (!store.Exists)
        {
            Console.WriteLine($"'{mod.Name}' is already enabled (no profile file, everything enabled).");
            return;
        }

        // Check the prospective state before committing anything.
        var prospective = new HashSet<string>(store.EnabledIdsOrAll()!, StringComparer.OrdinalIgnoreCase);
        prospective.Add(modId);

        if (!TryResolve(manifest, prospective, out var resolution))
            return;

        store.Enable(modId);
        Console.WriteLine($"Enabled {mod.Name} in profile '{store.Load().ActiveProfile}'.");

        ApplyInstallation(resolution, sourceDirectory, new ModInstaller(gameFolderPath));
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
            return;
        }

        var manifest = new ManifestReader().Read(manifestPath);
        var mod = FindMod(manifest, modId);
        if (mod is null)
            return;

        var store = new ProfilesStore(gameFolderPath);

        ISet<string> prospective;
        if (store.Exists)
        {
            prospective = new HashSet<string>(store.EnabledIdsOrAll()!, StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            // No profile yet means "everything on" — disabling the first mod
            // seeds a default profile with everything except this one.
            prospective = new HashSet<string>(manifest.Mods.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
        }
        prospective.Remove(modId);

        // Disabling must not break a dependency some other enabled mod relies on.
        if (!TryResolve(manifest, prospective, out _))
            return;

        if (store.Exists)
        {
            store.Disable(modId);
        }
        else
        {
            store.Save(new ProfilesState(
                "default",
                new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["default"] = prospective.ToList()
                }));
        }

        Console.WriteLine($"Disabled {mod.Name}.");

        var installer = new ModInstaller(gameFolderPath);
        if (installer.IsInstalled(mod.Name))
        {
            var result = installer.Uninstall(mod.Name);
            if (result.Success)
            {
                Console.WriteLine($"  Removed {mod.Name}'s files from the game.");
                foreach (var warning in result.Warnings)
                    Console.WriteLine($"  Warning: {warning}");
            }
            else
            {
                Console.WriteLine($"  Could not remove files: {result.Error}");
            }
        }
    }

    private static ModEntry? FindMod(ModListManifest manifest, string modId)
    {
        var mod = manifest.Mods.FirstOrDefault(m => m.Id.Equals(modId, StringComparison.OrdinalIgnoreCase));
        if (mod is null)
            Console.WriteLine($"No mod with id '{modId}' in the manifest.");
        return mod;
    }

    private static bool TryResolve(ModListManifest manifest, ISet<string> enabledIds, out ResolutionResult resolution)
    {
        resolution = new ModListResolver().Resolve(manifest, enabledIds);
        if (resolution.IsValid)
            return true;

        Console.WriteLine("That change makes the profile invalid:");
        foreach (var error in resolution.Errors)
            Console.WriteLine($"  - {error}");
        return false;
    }

    private static void ApplyInstallation(
        ResolutionResult resolution,
        string sourceDirectory,
        ModInstaller installer)
    {
        foreach (var mod in resolution.OrderedMods)
        {
            if (installer.IsInstalled(mod.Name))
                continue;

            var result = installer.Install(mod, sourceDirectory);
            Console.WriteLine(result.Success
                ? $"  Installed {mod.Name}: {result.InstalledPaths!.Count} file(s)."
                : $"  Failed to install {mod.Name}: {result.Error}");
        }
    }
}