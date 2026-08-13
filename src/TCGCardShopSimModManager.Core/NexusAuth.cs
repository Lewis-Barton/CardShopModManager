using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace TCGCardShopSimModManager.Core;

/// <summary>
/// Supplies the HTTP Authorization header <see cref="NexusApi"/> needs. Two
/// kinds exist: the classic API-key (dev prototype only) and OAuth (the
/// production path for public distribution). OAuth tokens are loaded from
/// <see cref="NexusTokenStore"/> and refreshed on demand.
///
/// The header name differs between the two: the API key uses the custom
/// <c>apikey</c> header; OAuth uses <c>Authorization: Bearer ...</c>. Both are
/// opaque to callers — they just await <see cref="GetHeaderValueAsync"/>.
/// </summary>
public sealed class NexusAuth
{
    private readonly Func<Task<string>> _headerValue;

    private NexusAuth(string headerName, Func<Task<string>> headerValue, NexusUser? user)
    {
        HeaderName = headerName;
        _headerValue = headerValue;
        User = user;
    }

    /// <summary>The HTTP header the value goes into ("apikey" or "Authorization").</summary>
    public string HeaderName { get; }

    /// <summary>For OAuth, the signed-in user decoded from the access token. Null for API-key.</summary>
    public NexusUser? User { get; }

    public Task<string> GetHeaderValueAsync() => _headerValue();

    /// <summary>Dev-only classic API key. Throws a helpful error if no key is stored.</summary>
    public static NexusAuth FromApiKey(Func<string?> keyProvider) => new("apikey",
        async () =>
        {
            var key = keyProvider();
            if (key is null)
                throw new DownloadException(
                    "No Nexus API key stored. Run 'nexus set-key <apikey>' first.", retryable: false);
            return key;
        },
        user: null);

    public static NexusAuth FromApiKey(string key) => FromApiKey(() => key);

    /// <summary>OAuth: loads the stored token set and refreshes it when expired.</summary>
    public static NexusAuth FromOAuthStore(HttpClient? http = null, string? clientId = null)
    {
        var set = NexusTokenStore.TryLoad();
        var user = set is null ? null : NexusJwt.DecodeAccessToken(set.AccessToken);
        return new("Authorization",
            async () => "Bearer " + await NexusOAuth.GetValidAccessTokenAsync(http, clientId),
            user);
    }

    /// <summary>
    /// Prefer OAuth if a token is stored, otherwise fall back to the dev API key.
    /// Used by the real download paths so both flows keep working.
    /// </summary>
    public static NexusAuth Unified(HttpClient? http = null, string? clientId = null)
    {
        if (NexusTokenStore.Exists)
            return FromOAuthStore(http, clientId);

        var key = ApiKeyStore.TryLoad();
        if (key is not null)
            return FromApiKey(key);

        return new("Authorization",
            async () =>
            {
                if (NexusTokenStore.Exists)
                    return "Bearer " + await NexusOAuth.GetValidAccessTokenAsync(http, clientId);
                var k = ApiKeyStore.TryLoad();
                if (k is not null) return k;
                throw new DownloadException(
                    "No Nexus credentials. Run 'nexus login' (OAuth) or 'nexus set-key <apikey>' (dev) first.",
                    retryable: false);
            },
            user: null);
    }
}
