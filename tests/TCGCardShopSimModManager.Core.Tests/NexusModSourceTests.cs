using System.Security.Cryptography;
using System.Text.Json;
using TCGCardShopSimModManager.Core;

namespace TCGCardShopSimModManager.Core.Tests;

public sealed class NexusModSourceTests : IDisposable
{
    private readonly string _root;
    private readonly string _archives;
    private readonly LocalHttpServer _server = new();

    public NexusModSourceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nexus-tests-" + Guid.NewGuid().ToString("N"));
        _archives = Path.Combine(_root, "archives");
        Directory.CreateDirectory(_archives);
    }

    public void Dispose()
    {
        _server.Dispose();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task PremiumUser_ResolvesFileByName_AndDownloadsVerifiedBytes()
    {
        var payload = MakePayload(512);
        File.WriteAllBytes(Path.Combine(_archives, "loose-plugin.zip"), payload);

        var files = new List<(long ModId, long FileId, string FileName)> { (4000, 7000, "loose-plugin.zip") };
        _server.Provider = NexusMock.MakeProvider(_archives, "tcgcardshopsimulator", _server.Url(""), files, premium: true);

        // No nexusFileId: the file must be found via files.json by file_name.
        var mod = Ref("loose-plugin.zip", payload, nexusModId: 4000, nexusFileId: null);

        var result = await Download(mod);

        Assert.True(result.Success, result.Error);
        Assert.Equal(payload, File.ReadAllBytes(Path.Combine(_root, "out", "loose-plugin.zip")));
    }

    [Fact]
    public async Task PremiumUser_WithExplicitFileId_SkipsFileLookup()
    {
        var payload = MakePayload(256);
        File.WriteAllBytes(Path.Combine(_archives, "patch.zip"), payload);

        // files.json only knows a DIFFERENT file_name, so a lookup for
        // "patch.zip" would fail. The explicit file id must go straight to the
        // download_link endpoint and never consult files.json.
        _server.Provider = request =>
        {
            if (request.Path == "/v1/users/validate.json")
                return Json(new { user_id = 1, name = "T", is_premium = "true" });

            if (request.Path == "/v1/games/tcgcardshopsimulator/mods/4000/files.json")
                return Json(new[] { new { file_id = 7000L, file_name = "wrong-name.zip", category_name = "MAIN" } });

            if (request.Path == "/v1/games/tcgcardshopsimulator/mods/4000/files/7777/download_link.json")
                return Json(new[] { new { URI = $"{_server.Url("")}patch.zip", name = "patch.zip" } });

            if (request.Path == "/patch.zip")
                return new HttpResponse(200, payload, null);

            return new HttpResponse(404, Array.Empty<byte>(), null);
        };

        var mod = Ref("patch.zip", payload, nexusModId: 4000, nexusFileId: 7777);
        var result = await Download(mod);

        Assert.True(result.Success, result.Error);
        Assert.Equal(payload, File.ReadAllBytes(Path.Combine(_root, "out", "patch.zip")));
    }

    [Fact]
    public async Task FreeAccount_GetsManualDownloadGuidance()
    {
        var payload = MakePayload(64);
        File.WriteAllBytes(Path.Combine(_archives, "loose-plugin.zip"), payload);
        var files = new List<(long, long, string)> { (4000, 7000, "loose-plugin.zip") };
        _server.Provider = NexusMock.MakeProvider(_archives, "tcgcardshopsimulator", _server.Url(""), files, premium: false);

        var result = await Download(Ref("loose-plugin.zip", payload, nexusModId: 4000));

        Assert.False(result.Success);
        Assert.Contains("not premium", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nexusmods.com", result.Error);
        Assert.False(File.Exists(Path.Combine(_root, "out", "loose-plugin.zip")));
    }

    [Fact]
    public async Task MissingApiKey_FailsWithGuidance()
    {
        var payload = MakePayload(64);
        File.WriteAllBytes(Path.Combine(_archives, "loose-plugin.zip"), payload);
        _server.Provider = NexusMock.MakeProvider(_archives, "tcgcardshopsimulator", _server.Url(""),
            new List<(long, long, string)> { (4000, 7000, "loose-plugin.zip") });

        var source = new NexusModSource(_server.Url("v1"), "tcgcardshopsimulator", NexusAuth.FromApiKey(() => null));
        var result = await new ModDownloader(source, new DownloadOptions { RetryBaseDelayMs = 10 })
            .DownloadAsync(Ref("loose-plugin.zip", payload, nexusModId: 4000), Path.Combine(_root, "out"));

        Assert.False(result.Success);
        Assert.Contains("set-key", result.Error);
    }

    [Fact]
    public async Task MissingNexusModId_FailsClearly()
    {
        var payload = MakePayload(32);
        var result = await Download(Ref("loose-plugin.zip", payload, nexusModId: null));

        Assert.False(result.Success);
        Assert.Contains("nexusModId", result.Error);
    }

    [Fact]
    public async Task ArchivedOrWrongIds_ReportsNotFound()
    {
        var payload = MakePayload(64);
        File.WriteAllBytes(Path.Combine(_archives, "loose-plugin.zip"), payload);

        // Premium validate works, but the download_link call is archived/404.
        _server.Provider = request => request.Path switch
        {
            "/v1/users/validate.json" => Json(new { user_id = 1, name = "T", is_premium = "true" }),
            _ => new HttpResponse(404, Array.Empty<byte>(), null)
        };

        var result = await Download(Ref("loose-plugin.zip", payload, nexusModId: 4000, nexusFileId: 9999));

        Assert.False(result.Success);
        Assert.Contains("archived", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RateLimit_ThrowsRetryable_WithRetryAfter()
    {
        var calls = 0;
        _server.Provider = request =>
        {
            calls++;
            if (request.Path != "/v1/users/validate.json")
                return new HttpResponse(404, Array.Empty<byte>(), null);

            return calls == 1
                ? new HttpResponse(429, Array.Empty<byte>(), null, RetryAfter: "1")
                : Json(new { user_id = 1, name = "T", is_premium = "true" });
        };

        var api = new NexusApi(_server.Url("v1"), "tcgcardshopsimulator", "test-agent");

        var limited = await Assert.ThrowsAsync<DownloadException>(() => api.GetUserAsync(NexusAuth.FromApiKey("key"), CancellationToken.None));
        Assert.True(limited.Retryable);
        Assert.Equal(1, limited.RetryAfterSeconds);

        var user = await api.GetUserAsync(NexusAuth.FromApiKey("key"), CancellationToken.None);
        Assert.True(user.IsPremium);
    }

    [Fact]
    public async Task AuthoringMetadata_ReadsModAndExactFileDetails()
    {
        _server.Provider = request => request.Path switch
        {
            "/v1/games/tcgcardshopsimulator/mods/4000.json" => Json(new
            {
                mod_id = 4000L,
                name = "Better Shelves"
            }),
            "/v1/games/tcgcardshopsimulator/mods/4000/files/7000.json" => Json(new
            {
                file_id = 7000L,
                file_name = "better-shelves.zip",
                version = "2.1.0",
                size_in_bytes = 12345L
            }),
            _ => new HttpResponse(404, Array.Empty<byte>(), null)
        };
        using var api = new NexusApi(_server.Url("v1"), "tcgcardshopsimulator", "test-agent");
        var auth = NexusAuth.FromApiKey("key");

        var mod = await api.GetModInfoAsync(4000, auth, CancellationToken.None);
        var file = await api.GetFileInfoAsync(4000, 7000, auth, CancellationToken.None);

        Assert.Equal(new NexusModInfo(4000, "Better Shelves"), mod);
        Assert.Equal(new NexusFileInfo(7000, "better-shelves.zip", "2.1.0", 12345), file);
    }

    [Fact]
    public async Task ApiKeyStore_RoundTripAndDelete()
    {
        try
        {
            ApiKeyStore.Save("secret-key-123");
            Assert.True(ApiKeyStore.Exists);
            Assert.Equal("secret-key-123", ApiKeyStore.TryLoad());

            ApiKeyStore.Delete();
            Assert.False(ApiKeyStore.Exists);
            Assert.Null(ApiKeyStore.TryLoad());
        }
        finally
        {
            ApiKeyStore.Delete();
        }
    }

    // --- helpers -----------------------------------------------------------

    private async Task<DownloadResult> Download(ModReference mod)
    {
        var source = new NexusModSource(_server.Url("v1"), "tcgcardshopsimulator", NexusAuth.FromApiKey("test-key"));
        return await new ModDownloader(source, new DownloadOptions { RetryBaseDelayMs = 10 })
            .DownloadAsync(mod, Path.Combine(_root, "out"));
    }

    private static ModReference Ref(string fileName, byte[] content, long? nexusModId, long? nexusFileId = null) =>
        new("test-mod", fileName, Sha(content), null, nexusModId, nexusFileId);

    private static HttpResponse Json(object value) =>
        new(200, JsonSerializer.SerializeToUtf8Bytes(value), null);

    private static byte[] MakePayload(int length)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
            bytes[i] = (byte)(i % 251);
        return bytes;
    }

    private static string Sha(byte[] content)
    {
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(content)).ToLowerInvariant();
    }
}
