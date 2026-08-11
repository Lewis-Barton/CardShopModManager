using System.Security.Cryptography;
using System.Text;

namespace CardShopModManager.Core;

/// <summary>
/// Stores the Nexus API key so only the signed-in user can read it.
///
/// On Windows the blob is encrypted with DPAPI scoped to the current user. On
/// other platforms DPAPI does not exist, so the bytes are stored plainly (the
/// OS account barrier is the only protection) — better than crash; a real
/// cross-platform secure store would be a follow-up.
/// </summary>
public static class ApiKeyStore
{
    private static string StoragePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CardShopModManager",
            "nexus-key.bin");

    public static bool Exists => File.Exists(StoragePath);

    public static void Save(string apiKey)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StoragePath)!);
        File.WriteAllBytes(StoragePath, Encrypt(Encoding.UTF8.GetBytes(apiKey)));
    }

    public static string? TryLoad()
    {
        if (!File.Exists(StoragePath))
            return null;

        try
        {
            return Encoding.UTF8.GetString(Decrypt(File.ReadAllBytes(StoragePath)));
        }
        catch (CryptographicException)
        {
            // Corrupt or foreign blob — treat as "no key stored".
            return null;
        }
    }

    public static void Delete()
    {
        if (File.Exists(StoragePath))
            File.Delete(StoragePath);
    }

    private static byte[] Encrypt(byte[] plainBytes) =>
        OperatingSystem.IsWindows()
            ? ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser)
            : plainBytes;

    private static byte[] Decrypt(byte[] storedBytes) =>
        OperatingSystem.IsWindows()
            ? ProtectedData.Unprotect(storedBytes, null, DataProtectionScope.CurrentUser)
            : storedBytes;
}