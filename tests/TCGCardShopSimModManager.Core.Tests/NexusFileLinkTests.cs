using TCGCardShopSimModManager.Core;

namespace TCGCardShopSimModManager.Core.Tests;

public sealed class NexusFileLinkTests
{
    [Theory]
    [InlineData("https://www.nexusmods.com/tcgcardshopsimulator/mods/123?tab=files&file_id=456", 123, 456)]
    [InlineData("https://nexusmods.com/tcgcardshopsimulator/mods/123?file_id=456&tab=files", 123, 456)]
    [InlineData("nxm://tcgcardshopsimulator/mods/123/files/456?key=value", 123, 456)]
    [InlineData("nexus:123:456", 123, 456)]
    public void TryParse_ExactFileLinks_ReturnsIds(string value, long modId, long fileId)
    {
        Assert.True(NexusFileLink.TryParse(value, out var link));
        Assert.NotNull(link);
        Assert.Equal(modId, link!.ModId);
        Assert.Equal(fileId, link.FileId);
    }

    [Theory]
    [InlineData("https://www.nexusmods.com/tcgcardshopsimulator/mods/698?tab=files", 698)]
    [InlineData("698", 698)]
    [InlineData("nexus:698", 698)]
    public void ModLinkTryParse_PageOrStableSelector_ReturnsModId(string value, long modId)
    {
        Assert.True(NexusModLink.TryParse(value, out var link));
        Assert.NotNull(link);
        Assert.Equal(modId, link!.ModId);
    }

    [Theory]
    [InlineData("https://www.nexusmods.com/api/files/29317446766498/download")]
    [InlineData("https://example.com/tcgcardshopsimulator/mods/698?tab=files")]
    [InlineData("nexus:not-a-number")]
    public void ModLinkTryParse_UnsupportedValue_ReturnsFalse(string value)
    {
        Assert.False(NexusModLink.TryParse(value, out var link));
        Assert.Null(link);
    }

    [Theory]
    [InlineData("https://www.nexusmods.com/tcgcardshopsimulator/mods/123")]
    [InlineData("https://example.com/tcgcardshopsimulator/mods/123?file_id=456")]
    [InlineData("nxm://tcgcardshopsimulator/mods/123")]
    [InlineData("not a link")]
    public void TryParse_NonFileLinks_ReturnsFalse(string value)
    {
        Assert.False(NexusFileLink.TryParse(value, out var link));
        Assert.Null(link);
    }
}
