using System.Text.Json;

namespace TCGCardShopSimModManager.Core;

/// <summary>
/// A stand-in Nexus v1 API for tests and the <c>nexus-demo</c> command: serves
/// the same JSON shapes the real API returns, so the real client code path is
/// exercised without an account. A single mod's file list is declared per mod.
/// </summary>
public static class NexusMock
{
    public static Func<HttpRequest, HttpResponse> MakeProvider(
        string archivesRoot,
        string gameDomain,
        string downloadBaseUrl,
        IReadOnlyList<(long ModId, long FileId, string FileName)> files,
        bool premium = true)
    {
        return request =>
        {
            if (request.Path == "/v1/users/validate.json")
            {
                return Json(new
                {
                    user_id = 1,
                    name = "Mock User",
                    key = "mock-key",
                    is_premium = premium ? "true" : "false"
                });
            }

            foreach (var (modId, fileId, fileName) in files)
            {
                if (request.Path == $"/v1/games/{gameDomain}/mods/{modId}/files.json")
                {
                    return Json(new[]
                    {
                        new { file_id = fileId, file_name = fileName, category_name = "MAIN" }
                    });
                }

                if (request.Path == $"/v1/games/{gameDomain}/mods/{modId}/files/{fileId}/download_link.json")
                {
                    return Json(new[]
                    {
                        new { URI = $"{downloadBaseUrl}/{fileName}", name = fileName }
                    });
                }
            }

            // Everything else is treated as a file download from the archive root.
            var path = Path.Combine(archivesRoot, request.Path.TrimStart('/'));
            if (!File.Exists(path))
                return new HttpResponse(404, Array.Empty<byte>(), null);

            return new HttpResponse(200, File.ReadAllBytes(path), null);
        };
    }

    private static HttpResponse Json(object value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        return new HttpResponse(200, bytes, null);
    }
}