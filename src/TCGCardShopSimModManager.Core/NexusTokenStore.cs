using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TCGCardShopSimModManager.Core;

/// <summary>
/// Stores the Nexus OAuth token set, encrypted per-user with DPAPI — the same
/// protection model as <see cref="ApiKeyStore"/>. On non-Windows the blob is
/// stored plainly (account barrier only); a real cross-platform store is a
/// follow-up.
/// </summary>
public static class NexusTokenStore
{
    private static string StoragePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TCGCardShopSimModManager",
            "nexus-oauth-tokens.bin");

    public static bool Exists => File.Exists(StoragePath);

    public static void Save(NexusTokenSet set)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StoragePath)!);
        var json = JsonSerializer.SerializeToUtf8Bytes(set);
        File.WriteAllBytes(StoragePath, Encrypt(json));
    }

    public static NexusTokenSet? TryLoad()
    {
        if (!File.Exists(StoragePath))
            return null;

        try
        {
            return JsonSerializer.Deserialize<NexusTokenSet>(Decrypt(File.ReadAllBytes(StoragePath)));
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static void Delete()
    {
        if (File.Exists(StoragePath))
            File.Delete(StoragePath);
    }

    private static byte[] Encrypt(byte[] plain) =>
        OperatingSystem.IsWindows()
            ? ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser)
            : plain;

    private static byte[] Decrypt(byte[] stored) =>
        OperatingSystem.IsWindows()
            ? ProtectedData.Unprotect(stored, null, DataProtectionScope.CurrentUser)
            : stored;
}
