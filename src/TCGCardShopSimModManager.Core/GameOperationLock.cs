using System.Security.Cryptography;
using System.Text;

namespace TCGCardShopSimModManager.Core;

internal sealed class GameOperationLock : IDisposable
{
    private readonly FileStream _stream;

    private GameOperationLock(FileStream stream) => _stream = stream;

    public static GameOperationLock Acquire(string gameFolderPath, TimeSpan? timeout = null)
    {
        var fullPath = Path.GetFullPath(gameFolderPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullPath))).ToLowerInvariant();
        var lockRoot = Path.Combine(Path.GetTempPath(), "cardshopmodmanager-locks");
        Directory.CreateDirectory(lockRoot);
        var lockPath = Path.Combine(lockRoot, $"{key}.lock");
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));

        while (true)
        {
            try
            {
                var stream = new FileStream(
                    lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                try
                {
                    DurableRecoveryTransaction.RecoverPending(gameFolderPath);
                    return new GameOperationLock(stream);
                }
                catch
                {
                    stream.Dispose();
                    throw;
                }
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new IOException(
                    "Another mod manager operation is already changing this game installation. Try again when it has finished.", ex);
            }
        }
    }

    public void Dispose() => _stream.Dispose();
}
