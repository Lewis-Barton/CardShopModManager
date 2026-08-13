using System.Text.Json;

namespace TCGCardShopSimModManager.Core;

/// <summary>
/// The persisted profile state for one game install.
/// </summary>
public sealed record ProfilesState(
    string? ActiveProfile,
    Dictionary<string, List<string>> Profiles)
{
    public List<string>? EnabledForActive()
    {
        if (ActiveProfile is null)
            return null;
        return Profiles.TryGetValue(ActiveProfile, out var ids) ? ids : null;
    }
}

/// <summary>
/// Named sets of enabled mod ids. Lives in the game folder next to the journal.
/// No profile file means "everything in the manifest is enabled".
/// </summary>
public sealed class ProfilesStore
{
    private const string FileName = "cardshopmodmanager.profiles.json";
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path;
    private readonly AtomicJsonFile<ProfilesState> _file;

    public ProfilesStore(string gameFolderPath)
    {
        _path = Path.Combine(gameFolderPath, FileName);
        _file = new AtomicJsonFile<ProfilesState>(_path, Options, Empty, recoverCorrupt: false);
    }

    public string FilePath => _path;

    public bool Exists => File.Exists(_path);

    public ProfilesState Load()
    {
        return _file.Read();
    }

    public void Save(ProfilesState state)
    {
        _file.Write(state);
    }

    /// <summary>Add an id to the active profile, creating one if needed.</summary>
    public void Enable(string modId)
    {
        _file.Update(state =>
        {
            var active = state.ActiveProfile ?? "default";
            state.Profiles.TryGetValue(active, out var ids);
            ids ??= new List<string>();
            if (!ids.Contains(modId, StringComparer.OrdinalIgnoreCase))
                ids.Add(modId);
            state.Profiles[active] = ids;
            return (state with { ActiveProfile = active }, true);
        });
    }

    /// <summary>Remove an id from the active profile.</summary>
    public void Disable(string modId)
    {
        _file.Update(state =>
        {
            if (state.ActiveProfile is not null && state.Profiles.TryGetValue(state.ActiveProfile, out var ids))
                ids.RemoveAll(id => id.Equals(modId, StringComparison.OrdinalIgnoreCase));
            return (state, true);
        });
    }

    /// <summary>Switch the active profile. Returns false if the name is unknown.</summary>
    public bool Use(string profileName)
    {
        return _file.Update(state => state.Profiles.ContainsKey(profileName)
            ? (state with { ActiveProfile = profileName }, true)
            : (state, false));
    }

    /// <summary>
    /// The ids enabled for the active profile, or null when there is no profile
    /// file (meaning: everything in the manifest is enabled).
    /// </summary>
    public ISet<string>? EnabledIdsOrAll()
    {
        if (!Exists)
            return null;

        var ids = Load().EnabledForActive();
        return ids is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
    }

    private static ProfilesState Empty() => new(null,
        new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase));
}
