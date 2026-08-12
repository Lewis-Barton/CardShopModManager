using TCGCardShopSimModManager.Core;

namespace TCGCardShopSimModManager.Core.Tests;

public sealed class DestinationConflictFinderTests
{
    private static InstallPlan Plan(string modName, params string[] destinations) =>
        new(
            new ModEntry(modName, modName, null, "x.zip", new string('0', 64),
                "BepInExPlugin", new List<string>(), new List<string>()),
            "test layout",
            destinations.Select(d => new ArchiveContentEntry(@"C:\fake\source", d, d)).ToList(),
            new List<string>(),
            new List<string>());

    [Fact]
    public void DistinctDestinations_ProduceNoConflicts()
    {
        var conflicts = DestinationConflictFinder.Find(new[]
        {
            Plan("mod-a", "BepInEx/plugins/a.dll"),
            Plan("mod-b", "BepInEx/plugins/b.dll")
        });

        Assert.Empty(conflicts);
    }

    [Fact]
    public void SameDestinationAcrossMods_IsReportedOnce()
    {
        var conflicts = DestinationConflictFinder.Find(new[]
        {
            Plan("mod-a", "BepInEx/plugins/shared.dll"),
            Plan("mod-b", "BepInEx/plugins/shared.dll")
        });

        var conflict = Assert.Single(conflicts);
        Assert.Equal("BepInEx/plugins/shared.dll", conflict.Destination);
        Assert.Equal("mod-a", conflict.ModA);
        Assert.Equal("mod-b", conflict.ModB);
    }

    [Fact]
    public void DuplicateInsideOneMod_IsStillReported()
    {
        // A destination claimed twice by one archive is also a conflict — the
        // install would refuse it anyway, so detect it here in the pre-flight.
        var conflicts = DestinationConflictFinder.Find(new[]
        {
            Plan("mod-a", "BepInEx/plugins/dup.dll", "BepInEx/plugins/dup.dll")
        });

        var conflict = Assert.Single(conflicts);
        Assert.Equal("mod-a", conflict.ModA);
        Assert.Equal("mod-a", conflict.ModB);
    }
}