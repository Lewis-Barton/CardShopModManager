namespace TCGCardShopSimModManager.Core;

public sealed record ProfileChangeResult(
    bool Success,
    string? Error,
    List<string> Messages,
    List<string> Warnings);

/// <summary>Applies profile file changes only after their mod files succeed.</summary>
public sealed class ProfileService
{
    private readonly ProfilesStore _store;
    private readonly ModInstaller _installer;
    private readonly string _gameFolderPath;

    public ProfileService(string gameFolderPath)
    {
        _gameFolderPath = gameFolderPath;
        _store = new ProfilesStore(gameFolderPath);
        _installer = new ModInstaller(gameFolderPath, disabledRoot: null, operationLockHeld: true);
    }

    public ProfileChangeResult Enable(ModListManifest manifest, string modId, string sourceDirectory)
    {
        try
        {
            using var operation = GameOperationLock.Acquire(_gameFolderPath);
            return EnableLocked(manifest, modId, sourceDirectory);
        }
        catch (IOException ex)
        {
            return Failure(ex.Message);
        }
    }

    private ProfileChangeResult EnableLocked(ModListManifest manifest, string modId, string sourceDirectory)
    {
        var mod = FindMod(manifest, modId);
        if (mod is null)
            return Failure($"No mod with id '{modId}' in the manifest.");
        if (!_store.Exists)
            return Success($"'{mod.Name}' is already enabled (no profile file, everything is enabled).");

        var state = Clone(_store.Load());
        var active = state.ActiveProfile ?? "default";
        state.Profiles.TryGetValue(active, out var ids);
        ids ??= new List<string>();
        if (!ids.Contains(modId, StringComparer.OrdinalIgnoreCase))
            ids.Add(modId);
        state.Profiles[active] = ids;
        state = state with { ActiveProfile = active };

        var resolution = new ModListResolver().Resolve(
            manifest, new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase));
        if (!resolution.IsValid)
            return Failure("That change makes the profile invalid: " + string.Join("; ", resolution.Errors));

        var installedHere = new List<ModEntry>();
        foreach (var candidate in resolution.OrderedMods)
        {
            if (_installer.IsInstalled(candidate.Name))
                continue;

            var install = _installer.Install(candidate, sourceDirectory);
            if (!install.Success)
            {
                RollBackInstalls(installedHere);
                return Failure($"Could not enable {mod.Name}: {install.Error}");
            }
            installedHere.Add(candidate);
        }

        try
        {
            _store.Save(state);
        }
        catch (Exception ex)
        {
            var warnings = RollBackInstalls(installedHere);
            return new ProfileChangeResult(false,
                $"The mod files were installed, but the profile could not be saved: {ex.Message}",
                new List<string>(), warnings);
        }

        return Success($"Enabled {mod.Name} in profile '{active}'.");
    }

    public ProfileChangeResult Disable(ModListManifest manifest, string modId, string sourceDirectory)
    {
        try
        {
            using var operation = GameOperationLock.Acquire(_gameFolderPath);
            return DisableLocked(manifest, modId, sourceDirectory);
        }
        catch (IOException ex)
        {
            return Failure(ex.Message);
        }
    }

    private ProfileChangeResult DisableLocked(ModListManifest manifest, string modId, string sourceDirectory)
    {
        var mod = FindMod(manifest, modId);
        if (mod is null)
            return Failure($"No mod with id '{modId}' in the manifest.");

        var state = Clone(_store.Load());
        var active = state.ActiveProfile ?? "default";
        if (!state.Profiles.TryGetValue(active, out var ids))
            ids = manifest.Mods.Select(entry => entry.Id).ToList();
        ids.RemoveAll(id => id.Equals(modId, StringComparison.OrdinalIgnoreCase));
        state.Profiles[active] = ids;
        state = state with { ActiveProfile = active };

        var resolution = new ModListResolver().Resolve(
            manifest, new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase));
        if (!resolution.IsValid)
            return Failure("That change makes the profile invalid: " + string.Join("; ", resolution.Errors));

        var removed = false;
        if (_installer.IsInstalled(mod.Name))
        {
            var uninstall = _installer.Uninstall(mod.Name);
            if (!uninstall.Success)
                return Failure($"Could not disable {mod.Name}: {uninstall.Error}");
            removed = true;
        }

        try
        {
            _store.Save(state);
        }
        catch (Exception ex)
        {
            var warnings = new List<string>();
            if (removed)
            {
                var reinstall = _installer.Install(mod, sourceDirectory);
                if (!reinstall.Success)
                    warnings.Add($"Could not restore {mod.Name}: {reinstall.Error}");
            }
            return new ProfileChangeResult(false,
                $"The mod files were removed, but the profile could not be saved: {ex.Message}",
                new List<string>(), warnings);
        }

        return Success($"Disabled {mod.Name}.");
    }

    private List<string> RollBackInstalls(IEnumerable<ModEntry> installed)
    {
        var warnings = new List<string>();
        foreach (var mod in installed.Reverse())
        {
            var result = _installer.Uninstall(mod.Name);
            if (!result.Success)
                warnings.Add($"Could not roll back {mod.Name}: {result.Error}");
        }
        return warnings;
    }

    private static ProfilesState Clone(ProfilesState state) => new(
        state.ActiveProfile,
        state.Profiles.ToDictionary(
            pair => pair.Key,
            pair => new List<string>(pair.Value),
            StringComparer.OrdinalIgnoreCase));

    private static ModEntry? FindMod(ModListManifest manifest, string modId) =>
        manifest.Mods.FirstOrDefault(mod => mod.Id.Equals(modId, StringComparison.OrdinalIgnoreCase));

    private static ProfileChangeResult Success(string message) =>
        new(true, null, new List<string> { message }, new List<string>());

    private static ProfileChangeResult Failure(string error) =>
        new(false, error, new List<string>(), new List<string>());
}
