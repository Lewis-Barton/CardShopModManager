using System.Text.Json;

namespace CardShopModManager.Core;

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

    public ProfilesStore(string gameFolderPath)
    {
        _path = Path.Combine(gameFolderPath, FileName);
    }

    public string FilePath => _path;

    public bool Exists => File.Exists(_path);

    public ProfilesState Load()
    {
        if (!File.Exists(_path))
            return new ProfilesState(null, new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase));

        var json = File.ReadAllText(_path);
        return JsonSerializer.Deserialize<ProfilesState>(json, Options)
               ?? new ProfilesState(null, new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase));
    }

    public void Save(ProfilesState state)
    {
        var json = JsonSerializer.Serialize(state, Options);
        File.WriteAllText(_path, json);
    }

    /// <summary>Add an id to the active profile, creating one if needed.</summary>
    public void Enable(string modId)
    {
        var state = Load();
        var active = state.ActiveProfile ?? "default";

        state.Profiles.TryGetValue(active, out var ids);
        ids ??= new List<string>();
        if (!ids.Contains(modId, StringComparer.OrdinalIgnoreCase))
            ids.Add(modId);

        state.Profiles[active] = ids;
        state = state with { ActiveProfile = active };
        Save(state);
    }

    /// <summary>Remove an id from the active profile.</summary>
    public void Disable(string modId)
    {
        var state = Load();
        if (state.ActiveProfile is null)
            return;

        if (state.Profiles.TryGetValue(state.ActiveProfile, out var ids))
        {
            ids.RemoveAll(id => id.Equals(modId, StringComparison.OrdinalIgnoreCase));
        }

        Save(state);
    }

    /// <summary>Switch the active profile. Returns false if the name is unknown.</summary>
    public bool Use(string profileName)
    {
        var state = Load();
        if (!state.Profiles.ContainsKey(profileName))
            return false;

        state = state with { ActiveProfile = profileName };
        Save(state);
        return true;
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
}