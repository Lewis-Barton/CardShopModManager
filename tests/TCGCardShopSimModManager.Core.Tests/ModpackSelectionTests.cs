using System.Text.Json;
using TCGCardShopSimModManager.Core;

namespace TCGCardShopSimModManager.Core.Tests;

public sealed class ModpackSelectionTests
{
    [Fact]
    public void Resolve_IncludesRequiredModsAndSelectedOptionalDependencies()
    {
        var required = Mod("required", required: true);
        var library = Mod("library", required: false);
        var optional = Mod("optional", required: false) with
        {
            Dependencies = new List<string> { "library" }
        };
        var manifest = Manifest(required, library, optional);

        var result = ModpackSelection.Resolve(manifest, ["optional"]);

        Assert.True(result.IsValid, string.Join("\n", result.Errors));
        Assert.Equal(["required", "library", "optional"],
            result.Manifest!.Mods.Select(mod => mod.Id));
    }

    [Fact]
    public void Resolve_LeavesUnselectedOptionalModsOut()
    {
        var result = ModpackSelection.Resolve(
            Manifest(Mod("required", true), Mod("optional", false)),
            Array.Empty<string>());

        Assert.True(result.IsValid, string.Join("\n", result.Errors));
        Assert.Equal("required", Assert.Single(result.Manifest!.Mods).Id);
    }

    [Fact]
    public void Resolve_RejectsUnknownSelection()
    {
        var result = ModpackSelection.Resolve(Manifest(Mod("required", true)), ["missing"]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("not in the modpack"));
    }

    [Fact]
    public void MissingRequiredProperty_DefaultsToTrue()
    {
        const string json = """
            {
              "manifestVersion": 1,
              "name": "Legacy pack",
              "game": "tcgcardshopsimulator",
              "mods": [{
                "id": "legacy",
                "name": "Legacy",
                "version": "1.0.0",
                "archive": "legacy.zip",
                "sha256": "abc",
                "installType": "BepInExPlugin",
                "dependencies": [],
                "conflicts": []
              }]
            }
            """;

        var manifest = JsonSerializer.Deserialize<ModListManifest>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.True(Assert.Single(manifest!.Mods).Required);
    }

    private static ModListManifest Manifest(params ModEntry[] mods) =>
        new(1, "Pack", "tcgcardshopsimulator", mods.ToList());

    private static ModEntry Mod(string id, bool required) =>
        new(id, id, "1.0.0", $"{id}.zip", "abc", "BepInExPlugin",
            new List<string>(), new List<string>(), Required: required);
}
