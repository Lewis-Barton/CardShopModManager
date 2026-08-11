namespace CardShopModManager.Core;

public sealed class GameDetector
{
    private const string GameExecutableName = "Card Shop Simulator.exe";

    public GameDetectionResult Detect(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            return new GameDetectionResult(false, null, $"Folder does not exist: {folderPath}");
        }

        var exePath = Path.Combine(folderPath, GameExecutableName);
        if (!File.Exists(exePath))
        {
            return new GameDetectionResult(
                false, null, $"Game executable not found in {folderPath}");
        }

        return new GameDetectionResult(true, exePath, null);
    }
}