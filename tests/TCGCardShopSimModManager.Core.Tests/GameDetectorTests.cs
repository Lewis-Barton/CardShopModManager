using TCGCardShopSimModManager.Core;
using Xunit;

public class GameDetectorTests
{
    [Fact]
    public void Detect_ReturnsInvalid_WhenFolderDoesNotExist()
    {
        var detector = new GameDetector();
        var result = detector.Detect("Z:\\definitely\\not\\real");
        Assert.False(result.IsValid);
    }
}