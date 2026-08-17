using TCGCardShopSimModManager.Core;

namespace TCGCardShopSimModManager.Core.Tests;

public sealed class GameCompatibilityTests
{
    [Fact]
    public void Evaluate_MarksMatchingBuildCompatible()
    {
        var result = GameCompatibility.Evaluate(["100", "200"], "200");

        Assert.Equal(GameCompatibilityStatus.Compatible, result.Status);
        Assert.False(result.MayBeUnsupported);
    }

    [Fact]
    public void Evaluate_MarksDifferentBuildUnsupported()
    {
        var result = GameCompatibility.Evaluate(["100"], "200");

        Assert.Equal(GameCompatibilityStatus.Incompatible, result.Status);
        Assert.True(result.MayBeUnsupported);
    }

    [Fact]
    public void Evaluate_DistinguishesUnknownBuildAndMissingDeclaration()
    {
        Assert.Equal(
            GameCompatibilityStatus.InstalledBuildUnknown,
            GameCompatibility.Evaluate(["100"], null).Status);
        Assert.Equal(
            GameCompatibilityStatus.NotDeclared,
            GameCompatibility.Evaluate(null, "100").Status);
    }
}
