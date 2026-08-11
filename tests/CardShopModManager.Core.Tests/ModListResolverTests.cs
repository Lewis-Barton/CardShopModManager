using CardShopModManager.Core;

namespace CardShopModManager.Core.Tests;

public sealed class ModListResolverTests
{
    private static ModEntry Mod(string id, string[]? dependencies = null, string[]? conflicts = null) =>
        new(id, id, "1.0.0", $"{id}.zip", new string('0', 64), "BepInExPlugin",
            dependencies?.ToList() ?? new List<string>(),
            conflicts?.ToList() ?? new List<string>());

    private static ModListManifest List(params ModEntry[] mods) =>
        new(1, "Test List", "tcgcardshopsimulator", mods.ToList());

    private static ISet<string> All(ModListManifest manifest) =>
        new HashSet<string>(manifest.Mods.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void OrdersDependenciesBeforeDependents()
    {
        var plugin = Mod("plugin", dependencies: new[] { "library", "core" });
        var manifest = List(plugin, Mod("library"), Mod("core"));

        var result = new ModListResolver().Resolve(manifest, All(manifest));

        Assert.True(result.IsValid);
        var order = result.OrderedMods.Select(m => m.Id).ToList();
        Assert.Equal(3, order.Count);
        Assert.True(order.IndexOf("library") < order.IndexOf("plugin"));
        Assert.True(order.IndexOf("core") < order.IndexOf("plugin"));
    }

    [Fact]
    public void ReportsDependencyNotInTheList()
    {
        var manifest = List(Mod("plugin", dependencies: new[] { "ghost" }));

        var result = new ModListResolver().Resolve(manifest, All(manifest));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("depends on 'ghost', which is not in the mod list"));
    }

    [Fact]
    public void ReportsDependencyThatIsNotEnabled()
    {
        var library = Mod("library");
        var plugin = Mod("plugin", dependencies: new[] { "library" });
        var manifest = List(plugin, library);
        var enabledOnlyPlugin = new HashSet<string> { "plugin" };

        var result = new ModListResolver().Resolve(manifest, enabledOnlyPlugin);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("not enabled in this profile"));
    }

    [Fact]
    public void DetectsCircularDependencies()
    {
        var manifest = List(
            Mod("mod-a", dependencies: new[] { "mod-b" }),
            Mod("mod-b", dependencies: new[] { "mod-a" }));

        var result = new ModListResolver().Resolve(manifest, All(manifest));

        Assert.False(result.IsValid);
        var cycleError = Assert.Single(result.Errors);
        Assert.Contains("Circular dependency", cycleError);
        Assert.Contains("mod-a", cycleError);
        Assert.Contains("mod-b", cycleError);
    }

    [Fact]
    public void DetectsASelfDependencyAsACycle()
    {
        var manifest = List(Mod("mod-a", dependencies: new[] { "mod-a" }));

        var result = new ModListResolver().Resolve(manifest, All(manifest));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Circular dependency"));
    }

    [Fact]
    public void ReportsConflictsBetweenEnabledMods()
    {
        var manifest = List(
            Mod("mod-a", conflicts: new[] { "mod-b" }),
            Mod("mod-b"));

        var result = new ModListResolver().Resolve(manifest, All(manifest));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("conflict"));
    }

    [Fact]
    public void IgnoresConflictWhenTheOtherModIsDisabled()
    {
        var manifest = List(
            Mod("mod-a", conflicts: new[] { "mod-b" }),
            Mod("mod-b"));
        var enabledOnlyA = new HashSet<string> { "mod-a" };

        var result = new ModListResolver().Resolve(manifest, enabledOnlyA);

        Assert.True(result.IsValid);
        Assert.Equal("mod-a", Assert.Single(result.OrderedMods).Id);
    }

    [Fact]
    public void ReportsDuplicateIdsEvenWhenDisabled()
    {
        var manifest = List(Mod("mod-a"), Mod("mod-a"));
        var enabledEmpty = new HashSet<string>();

        var result = new ModListResolver().Resolve(manifest, enabledEmpty);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Duplicate mod id"));
    }

    [Fact]
    public void DisabledCycleParticipantsDoNotBlockEnabledMods()
    {
        var manifest = List(
            Mod("mod-a", dependencies: new[] { "mod-b" }),
            Mod("mod-b", dependencies: new[] { "mod-a" }),
            Mod("independent"));
        var enabledOnlyIndependent = new HashSet<string> { "independent" };

        var result = new ModListResolver().Resolve(manifest, enabledOnlyIndependent);

        Assert.True(result.IsValid);
        Assert.Equal("independent", Assert.Single(result.OrderedMods).Id);
    }

    [Fact]
    public void EmptyEnabledSetIsValidButInstallsNothing()
    {
        var manifest = List(Mod("mod-a"));
        var enabledNone = new HashSet<string>();

        var result = new ModListResolver().Resolve(manifest, enabledNone);

        Assert.True(result.IsValid);
        Assert.Empty(result.OrderedMods);
    }
}