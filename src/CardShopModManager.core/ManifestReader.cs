using System.Text.Json;

namespace CardShopModManager.Core;

public sealed class ManifestReader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ModListManifest Read(string manifestPath)
    {
        var json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<ModListManifest>(json, Options);

        if (manifest is null)
            throw new InvalidOperationException($"Failed to parse manifest: {manifestPath}");

        // A manifest that omits "dependencies"/"conflicts" deserializes those
        // lists as null; treat "not declared" as "empty" so the resolver can
        // assume every mod has a real list.
        return manifest with
        {
            Mods = manifest.Mods
                .Select(m => m with
                {
                    Dependencies = m.Dependencies ?? new List<string>(),
                    Conflicts = m.Conflicts ?? new List<string>()
                })
                .ToList()
        };
    }
}