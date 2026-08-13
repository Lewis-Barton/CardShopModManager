namespace TCGCardShopSimModManager.Core;

internal static class TemporaryDirectory
{
    public static void DeleteBestEffort(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Temporary cleanup must not replace the operation's real result.
        }
    }
}
