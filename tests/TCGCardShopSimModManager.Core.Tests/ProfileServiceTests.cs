using System.Security.Cryptography;
using TCGCardShopSimModManager.Core;

namespace TCGCardShopSimModManager.Core.Tests;

public sealed class ProfileServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "profile-service-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _game;
    private readonly string _source;

    public ProfileServiceTests()
    {
        _game = Path.Combine(_root, "game");
        _source = Path.Combine(_root, "source");
        Directory.CreateDirectory(_game);
        Directory.CreateDirectory(_source);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Enable_InstallsBeforeSavingProfile()
    {
        var mod = AddMod("example", "Example.dll", "dll-bytes");
        var manifest = Manifest(mod);
        new ProfilesStore(_game).Save(new ProfilesState(
            "default", new Dictionary<string, List<string>> { ["default"] = new() }));

        var result = new ProfileService(_game).Enable(manifest, mod.Id, _source);

        Assert.True(result.Success, result.Error);
        Assert.True(File.Exists(Path.Combine(_game, "BepInEx", "plugins", mod.Name, mod.Archive)));
        Assert.Contains(mod.Id, new ProfilesStore(_game).Load().EnabledForActive()!);
    }

    [Fact]
    public void Enable_FailedInstallLeavesProfileUnchanged()
    {
        var mod = AddMod("example", "Example.dll", "dll-bytes") with { Sha256 = new string('0', 64) };
        var store = new ProfilesStore(_game);
        store.Save(new ProfilesState(
            "default", new Dictionary<string, List<string>> { ["default"] = new() }));

        var result = new ProfileService(_game).Enable(Manifest(mod), mod.Id, _source);

        Assert.False(result.Success);
        Assert.Empty(store.Load().EnabledForActive()!);
        Assert.Empty(new JournalStore(_game).Load());
    }

    [Fact]
    public void Disable_RequiredDependencyLeavesFilesAndProfileUnchanged()
    {
        var dependency = AddMod("dependency", "Dependency.dll", "dependency");
        var plugin = AddMod("plugin", "Plugin.dll", "plugin") with
        {
            Dependencies = new List<string> { dependency.Id }
        };
        var manifest = Manifest(dependency, plugin);
        var installer = new ModInstaller(_game, Path.Combine(_root, "disabled"));
        Assert.True(installer.Install(dependency, _source).Success);
        Assert.True(installer.Install(plugin, _source).Success);
        var store = new ProfilesStore(_game);
        store.Save(new ProfilesState("default", new Dictionary<string, List<string>>
        {
            ["default"] = new() { dependency.Id, plugin.Id }
        }));

        var result = new ProfileService(_game).Disable(manifest, dependency.Id, _source);

        Assert.False(result.Success);
        Assert.True(installer.IsInstalled(dependency.Name));
        Assert.Contains(dependency.Id, store.Load().EnabledForActive()!);
    }

    [Fact]
    public void Disable_ModifiedFileLeavesProfileAndAllFilesUntouched()
    {
        var mod = AddMod("example", "Example.dll", "dll-bytes");
        var installer = new ModInstaller(_game, Path.Combine(_root, "disabled"));
        var install = installer.Install(mod, _source);
        Assert.True(install.Success);
        File.WriteAllText(Assert.Single(install.InstalledPaths!), "user-change");
        var store = new ProfilesStore(_game);
        store.Save(new ProfilesState("default", new Dictionary<string, List<string>>
        {
            ["default"] = new() { mod.Id }
        }));

        var result = new ProfileService(_game).Disable(Manifest(mod), mod.Id, _source);

        Assert.False(result.Success);
        Assert.Contains(mod.Id, store.Load().EnabledForActive()!);
        Assert.True(installer.IsInstalled(mod.Name));
    }

    private ModEntry AddMod(string id, string archive, string content)
    {
        var path = Path.Combine(_source, archive);
        File.WriteAllText(path, content);
        return new ModEntry(id, id, "1.0.0", archive, Sha(path), "BepInExPlugin",
            new List<string>(), new List<string>());
    }

    private static ModListManifest Manifest(params ModEntry[] mods) =>
        new(1, "Profile Test", "tcgcardshopsimulator", mods.ToList());

    private static string Sha(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }
}
