using CardShopModManager.Core;

namespace CardShopModManager.Core.Tests;

public sealed class ProfilesStoreTests : IDisposable
{
    private readonly string _gameFolder;
    private readonly ProfilesStore _store;

    public ProfilesStoreTests()
    {
        _gameFolder = Path.Combine(Path.GetTempPath(), "profiles-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_gameFolder);
        _store = new ProfilesStore(_gameFolder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_gameFolder))
            Directory.Delete(_gameFolder, recursive: true);
    }

    [Fact]
    public void MissingFile_ReportsNotExistsAndNoEnabledIds()
    {
        Assert.False(_store.Exists);
        Assert.Null(_store.EnabledIdsOrAll());
        Assert.Empty(_store.Load().Profiles);
    }

    [Fact]
    public void Enable_CreatesDefaultProfileAndPersists()
    {
        _store.Enable("mod-a");
        _store.Enable("mod-b");

        Assert.True(_store.Exists);
        var reloaded = new ProfilesStore(_gameFolder).Load();
        Assert.Equal("default", reloaded.ActiveProfile);
        Assert.Equal(new[] { "mod-a", "mod-b" }, reloaded.Profiles["default"]);
    }

    [Fact]
    public void Enable_IsIdempotent()
    {
        _store.Enable("mod-a");
        _store.Enable("mod-a");

        var ids = _store.EnabledIdsOrAll()!;
        Assert.Equal(new[] { "mod-a" }, ids);
    }

    [Fact]
    public void Disable_RemovesOnlyThatId()
    {
        _store.Enable("mod-a");
        _store.Enable("mod-b");

        _store.Disable("mod-a");

        Assert.Equal(new[] { "mod-b" }, new ProfilesStore(_gameFolder).Load().Profiles["default"]);
    }

    [Fact]
    public void Use_SwitchesActiveProfile()
    {
        _store.Enable("mod-a");
        _store.Save(new ProfilesStore(_gameFolder).Load() with
        {
            Profiles = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["default"] = new() { "mod-a" },
                ["pvp"] = new() { "mod-a", "mod-b" }
            }
        });

        Assert.True(_store.Use("pvp"));
        Assert.Equal(new[] { "mod-a", "mod-b" }, _store.EnabledIdsOrAll());
    }

    [Fact]
    public void Use_ReturnsFalseForUnknownProfile()
    {
        Assert.False(_store.Use("nope"));
    }
}