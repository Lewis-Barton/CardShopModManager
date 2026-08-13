using System.Collections.Generic;
using Xunit;
using TCGCardShopSimModManager.Core;

namespace TCGCardShopSimModManager.Core.Tests;

public sealed class ManifestValidatorTests
{
    private static ModListManifest Manifest(params ModEntry[] mods) =>
        new(1, "Test Pack", "tcgcardshopsimulator", new List<ModEntry>(mods));

    private static ModEntry Mod(string id, string installType, string archive) =>
        new(id, id, "1.0.0", archive, "abc", installType, new List<string>(), new List<string>());

    [Fact]
    public void Validate_AcceptsDotDotInsideFilename() // BUG-024
    {
        // ".." inside a filename is not a traversal — this must validate.
        var manifest = Manifest(Mod("example-mod", "BepInExPlugin", "MyMod..v1.zip"));
        var result = new ManifestValidator().Validate(manifest);
        Assert.True(result.IsValid, string.Join("\n", result.Errors));
    }

    [Fact]
    public void Validate_RejectsPathTraversalInArchive() // BUG-024
    {
        var manifest = Manifest(Mod("example-mod", "BepInExPlugin", "../escape.zip"));
        var result = new ManifestValidator().Validate(manifest);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("unsafe"));
    }

    [Fact]
    public void Validate_RejectsReservedBepInExTypeForNonFramework() // BUG-025
    {
        // "BepInEx" is reserved for the framework entry (id bepinex).
        var manifest = Manifest(Mod("evil", "BepInEx", "mod.zip"));
        var result = new ManifestValidator().Validate(manifest);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("reserved"));
    }

    [Fact]
    public void Validate_AcceptsFrameworkWithBepInExType() // BUG-032
    {
        var manifest = Manifest(Mod("bepinex", "BepInEx", "bepinex.zip"));
        var result = new ManifestValidator().Validate(manifest);
        Assert.True(result.IsValid, string.Join("\n", result.Errors));
    }

    [Fact]
    public void Validate_RejectsEmptyModsList() // BUG-028
    {
        var manifest = Manifest();
        var result = new ManifestValidator().Validate(manifest);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("no mods"));
    }
}
