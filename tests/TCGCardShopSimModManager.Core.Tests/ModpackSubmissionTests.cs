using System.IO;
using Xunit;

namespace TCGCardShopSimModManager.Core.Tests;

public sealed class ModpackSubmissionTests : IDisposable
{
    private readonly string _root;

    public ModpackSubmissionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "modpack-submission-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void ValidatePack_Passes_ForAWellFormedSubmission()
    {
        WriteValidPack("essential-qol");
        var result = new ModpackSubmissionValidator(_root).ValidatePack("essential-qol");
        Assert.True(result.IsValid, string.Join("\n", result.Errors));
    }

    [Fact]
    public void ValidatePack_Fails_WhenBepInExEntryMissing()
    {
        WritePackWithoutBepInEx();
        var result = new ModpackSubmissionValidator(_root).ValidatePack("no-bepinex");
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("BepInEx"));
    }

    [Fact]
    public void ValidatePack_Fails_WhenModHasNoSource()
    {
        WritePackWithUnsourcedMod();
        var result = new ModpackSubmissionValidator(_root).ValidatePack("unsourced");
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("no source"));
    }

    [Fact]
    public void ValidatePack_Fails_WhenLogoMissing()
    {
        WriteValidPack("essential-qol", withLogo: false);
        var result = new ModpackSubmissionValidator(_root).ValidatePack("essential-qol");
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Logo"));
    }

    [Fact]
    public void ValidateAll_ReportsEveryPack()
    {
        WriteValidPack("essential-qol");
        var results = new ModpackSubmissionValidator(_root).ValidateAll();
        var entry = Assert.Single(results);
        Assert.Equal("essential-qol", entry.PackId);
        Assert.True(entry.Result.IsValid);
    }

    // --- helpers ---------------------------------------------------------

    private void WriteValidPack(string id, bool withLogo = true)
    {
        File.WriteAllText(Path.Combine(_root, "index.json"), IndexJson(id));
        var packDir = Path.Combine(_root, id);
        Directory.CreateDirectory(packDir);
        File.WriteAllText(Path.Combine(packDir, "manifest.json"), ManifestJson());
        if (withLogo)
            File.WriteAllBytes(Path.Combine(packDir, "logo.png"), MakePng());
    }

    private void WritePackWithoutBepInEx()
    {
        var id = "no-bepinex";
        File.WriteAllText(Path.Combine(_root, "index.json"), IndexJson(id));
        var packDir = Path.Combine(_root, id);
        Directory.CreateDirectory(packDir);
        File.WriteAllText(Path.Combine(packDir, "manifest.json"), ManifestJsonNoBepInEx());
        File.WriteAllBytes(Path.Combine(packDir, "logo.png"), MakePng());
    }

    private void WritePackWithUnsourcedMod()
    {
        var id = "unsourced";
        File.WriteAllText(Path.Combine(_root, "index.json"), IndexJson(id));
        var packDir = Path.Combine(_root, id);
        Directory.CreateDirectory(packDir);
        File.WriteAllText(Path.Combine(packDir, "manifest.json"), ManifestJsonUnsourced());
        File.WriteAllBytes(Path.Combine(packDir, "logo.png"), MakePng());
    }

    private static string IndexJson(string id) =>
        "{\"version\":1,\"packs\":[{\"id\":\"" + id + "\",\"name\":\"Pack One\"," +
        "\"shortDescription\":\"desc\",\"logo\":\"" + id + "/logo.png\"," +
        "\"manifest\":\"" + id + "/manifest.json\",\"version\":\"1.0.0\"}]}";

    private static string ManifestJson() =>
        "{\"manifestVersion\":1,\"name\":\"Pack One\",\"game\":\"tcgcardshopsimulator\"," +
        "\"mods\":[" +
        "{\"id\":\"bepinex\",\"name\":\"BepInEx\",\"version\":\"5.4.23\",\"archive\":\"bepinex.zip\"," +
        "\"sha256\":\"abc\",\"installType\":\"BepInEx\",\"dependencies\":[],\"conflicts\":[]," +
        "\"downloadUrl\":\"https://example.com/bepinex.zip\"}," +
        "{\"id\":\"example-mod\",\"name\":\"Example Mod\",\"version\":\"1.0.0\",\"archive\":\"mod.zip\"," +
        "\"sha256\":\"abc\",\"installType\":\"BepInExPlugin\",\"dependencies\":[\"bepinex\"],\"conflicts\":[]," +
        "\"downloadUrl\":\"https://example.com/mod.zip\"}]}";

    private static string ManifestJsonNoBepInEx() =>
        "{\"manifestVersion\":1,\"name\":\"Pack One\",\"game\":\"tcgcardshopsimulator\"," +
        "\"mods\":[{\"id\":\"example-mod\",\"name\":\"Example Mod\",\"version\":\"1.0.0\",\"archive\":\"mod.zip\"," +
        "\"sha256\":\"abc\",\"installType\":\"BepInExPlugin\",\"dependencies\":[],\"conflicts\":[]," +
        "\"downloadUrl\":\"https://example.com/mod.zip\"}]}";

    private static string ManifestJsonUnsourced() =>
        "{\"manifestVersion\":1,\"name\":\"Pack One\",\"game\":\"tcgcardshopsimulator\"," +
        "\"mods\":[{\"id\":\"example-mod\",\"name\":\"Example Mod\",\"version\":\"1.0.0\",\"archive\":\"mod.zip\"," +
        "\"sha256\":\"abc\",\"installType\":\"BepInExPlugin\",\"dependencies\":[],\"conflicts\":[]}]}";

    private static byte[] MakePng()
    {
        // Valid PNG signature + padding so it clears the <1 KB placeholder warning.
        var bytes = new byte[2048];
        bytes[0] = 0x89; bytes[1] = 0x50; bytes[2] = 0x4E; bytes[3] = 0x47;
        bytes[4] = 0x0D; bytes[5] = 0x0A; bytes[6] = 0x1A; bytes[7] = 0x0A;
        return bytes;
    }
}
