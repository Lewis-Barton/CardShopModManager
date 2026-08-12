using TCGCardShopSimModManager.Core;

namespace TCGCardShopSimModManager.Core.Tests;

public sealed class ArchiveClassifierTests
{
    private static readonly ModEntry Mod = new(
        "example-mod", "Example Mod", null, "pack.zip", new string('0', 64),
        "BepInExPlugin", new List<string>(), new List<string>());

    private static ExtractedSource Source(string relativePath) =>
        new(relativePath, Path.Combine(@"C:\fake\extract", relativePath));

    [Fact]
    public void LooseDllAtRoot_BecomesPluginFolderLayout()
    {
        var plan = new ArchiveClassifier().BuildPlan(Mod, new[]
        {
            Source("ExampleMod.dll"),
            Source("README.md")
        });

        Assert.Contains("loose plugin folder", plan.LayoutName);
        var file = Assert.Single(plan.Files);
        Assert.Equal("BepInEx/plugins/Example Mod/ExampleMod.dll", file.DestinationRelativePath);
        var skip = Assert.Single(plan.SkippedEntries);
        Assert.Contains("README.md", skip);
    }

    [Fact]
    public void BepInExFolder_MirrorsIntoGameBepInEx()
    {
        var plan = new ArchiveClassifier().BuildPlan(Mod, new[]
        {
            Source("BepInEx/plugins/RealMod.dll"),
            Source("BepInEx/config/DumbSettings.cfg"),
            Source("BepInEx/patchers/CorePatch.dll")
        });

        Assert.Contains("BepInEx layout", plan.LayoutName);
        Assert.Equal(3, plan.Files.Count);
        Assert.Equal(
            "BepInEx/plugins/RealMod.dll",
            plan.Files.Single(f => f.SourceRelativePath == "BepInEx/plugins/RealMod.dll").DestinationRelativePath);
        Assert.Equal(
            "BepInEx/config/DumbSettings.cfg",
            plan.Files.Single(f => f.SourceRelativePath == "BepInEx/config/DumbSettings.cfg").DestinationRelativePath);
        Assert.Equal(
            "BepInEx/patchers/CorePatch.dll",
            plan.Files.Single(f => f.SourceRelativePath == "BepInEx/patchers/CorePatch.dll").DestinationRelativePath);
    }

    [Fact]
    public void PatcherFolder_UsesPatcherLayout()
    {
        var plan = new ArchiveClassifier().BuildPlan(Mod, new[]
        {
            Source("patchers/MyPatch.dll")
        });

        Assert.Contains("patcher layout", plan.LayoutName);
        var file = Assert.Single(plan.Files);
        Assert.Equal("BepInEx/patchers/MyPatch.dll", file.DestinationRelativePath);
    }

    [Fact]
    public void RootFilesWithoutStructure_MirrorIntoGameRoot()
    {
        var plan = new ArchiveClassifier().BuildPlan(Mod, new[]
        {
            Source("Data/Textures/card_back.png"),
            Source("mod.txt")
        });

        Assert.Contains("game root", plan.LayoutName);
        Assert.Equal(2, plan.Files.Count);
        Assert.Equal("Data/Textures/card_back.png", plan.Files[0].DestinationRelativePath);
        Assert.Equal("mod.txt", plan.Files[1].DestinationRelativePath);
    }

    [Fact]
    public void OnlyDocumentation_ProducesEmptyFileList()
    {
        var plan = new ArchiveClassifier().BuildPlan(Mod, new[]
        {
            Source("README.md"),
            Source("__MACOSX/something"),
            Source(".DS_Store")
        });

        Assert.Empty(plan.Files);
        Assert.Equal(3, plan.SkippedEntries.Count);
        Assert.Contains("nothing installable", plan.LayoutName);
    }

    [Fact]
    public void EmptySources_ProducesEmptyLayout()
    {
        var plan = new ArchiveClassifier().BuildPlan(Mod, Array.Empty<ExtractedSource>());
        Assert.Equal("empty archive", plan.LayoutName);
        Assert.Empty(plan.Files);
        Assert.Empty(plan.SkippedEntries);
    }
}