using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TCGCardShopSimModManager.Core;

/// <summary>
/// Nexus Mods OAuth 2.0 (PKCE, public client) helpers. The endpoints and
/// contract come from the official guide:
/// https://modding.wiki/en/api/oauth2-guide
///
/// We are a <b>public</b> application (a desktop mod manager), so we use PKCE
/// and never hold a client secret. The token endpoint is shared with the demo
/// app's <c>public_test</c> client until a dedicated app is registered.
/// </summary>
public static class NexusOAuth
{
    public const string AuthorizeEndpoint = "https://users.nexusmods.com/oauth/authorize";
    public const string TokenEndpoint = "https://users.nexusmods.com/oauth/token";

    /// <summary>Override with NEXUS_OAUTH_CLIENT_ID once a dedicated app is registered.</summary>
    public static string ClientId =>
        Environment.GetEnvironmentVariable("NEXUS_OAUTH_CLIENT_ID") ?? "public_test";

    /// <summary>Override with NEXUS_OAUTH_REDIRECT_URI to match a registered app.</summary>
    public static string RedirectUri =>
        Environment.GetEnvironmentVariable("NEXUS_OAUTH_REDIRECT_URI") ?? "http://127.0.0.1:8089/callback";

    public static string BuildAuthorizeUrl(
        string? redirectUri = null, string? state = null, string? codeChallenge = null, string? clientId = null)
    {
        redirectUri ??= RedirectUri;
        state ??= GenerateState();
        codeChallenge ??= ComputeCodeChallenge(GenerateCodeVerifier());
        clientId ??= ClientId;

        var query = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["response_type"] = "code",
            ["scope"] = "",
            ["redirect_uri"] = redirectUri,
            ["state"] = state,
            ["code_challenge_method"] = "S256",
            ["code_challenge"] = codeChallenge,
        };

        var joined = string.Join("&",
            new[] { "client_id", "response_type", "scope", "redirect_uri", "state", "code_challenge_method", "code_challenge" }
                .Select(k => $"{k}={Uri.EscapeDataString(query[k])}"));

        return AuthorizeEndpoint + "?" + joined;
    }

    /// <summary>A cryptographically random PKCE verifier (>= 43 chars).</summary>
    public static string GenerateCodeVerifier()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Base64Url.Encode(bytes); // 43 chars — satisfies the >= 43 minimum
    }

    /// <summary>S256 challenge: base64url(sha256(verifier)).</summary>
    public static string ComputeCodeChallenge(string verifier)
    {
        using var sha = SHA256.Create();
        return Base64Url.Encode(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
    }

    public static string GenerateState() => Base64Url.Encode(RandomBytes(16));

    public static async Task<NexusTokenSet> ExchangeCodeAsync(
        string code, string redirectUri, string codeVerifier, string? clientId = null, HttpClient? http = null)
    {
        http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri,
            ["scope"] = "",
            ["client_id"] = clientId ?? ClientId,
            ["code"] = code,
            ["code_verifier"] = codeVerifier,
        });

        using var response = await http.PostAsync(TokenEndpoint, content);
        if (!response.IsSuccessStatusCode)
            throw new DownloadException(
                $"Nexus OAuth authorization failed ({(int)response.StatusCode}). " +
                "Check the client id and redirect URI match what Nexus has registered.",
                retryable: false);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return ParseTokenSet(doc.RootElement);
    }

    public static async Task<NexusTokenSet> RefreshAsync(
        string refreshToken, string? clientId = null, HttpClient? http = null)
    {
        http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId ?? ClientId,
        });

        using var response = await http.PostAsync(TokenEndpoint, content);
        if (!response.IsSuccessStatusCode)
            throw new DownloadException(
                "Nexus refused to refresh the token (the user likely revoked access). Run 'nexus login' again.",
                retryable: false);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return ParseTokenSet(doc.RootElement);
    }

    /// <summary>Returns a valid access token, refreshing the stored one if expired.</summary>
    public static async Task<string> GetValidAccessTokenAsync(HttpClient? http = null, string? clientId = null)
    {
        var set = NexusTokenStore.TryLoad();
        if (set is null)
            throw new DownloadException("No Nexus OAuth token stored. Run 'nexus login' first.", retryable: false);

        if (!set.IsExpired)
            return set.AccessToken;

        var refreshed = await RefreshAsync(set.RefreshToken!, clientId, http);
        NexusTokenStore.Save(refreshed);
        return refreshed.AccessToken;
    }

    /// <summary>
    /// Runs the full desktop sign-in: starts a loopback listener, opens the
    /// browser, waits for the redirect, exchanges the code, and stores the token
    /// set. Returns the signed-in user decoded from the access token.
    /// </summary>
    public static async Task<NexusUser> LoginAsync(
        HttpClient? http = null, Action<string>? log = null, string? clientId = null)
    {
        clientId ??= ClientId;
        var redirectUri = RedirectUri;

        await using var listener = new LoopbackOAuthListener(redirectUri);
        await listener.StartAsync();

        var verifier = GenerateCodeVerifier();
        var state = GenerateState();
        var url = BuildAuthorizeUrl(redirectUri, state, ComputeCodeChallenge(verifier), clientId);

        log?.Invoke("Opening your browser to sign in to Nexus Mods...");
        OpenBrowser(url);

        log?.Invoke($"Waiting for Nexus to redirect back to {redirectUri} ...");
        LoopbackCallback result;
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120)))
        {
            try
            {
                result = await listener.WaitForCallbackAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                throw new DownloadException(
                    "Timed out waiting for Nexus to redirect back. The sign-in window may have been closed, " +
                    "or Nexus rejected the request before redirecting (e.g. an unknown client id). " +
                    "Check the browser for the error, and confirm NEXUS_OAUTH_CLIENT_ID and the redirect URI " +
                    "match what Nexus has registered.",
                    retryable: false);
            }
        }

        if (result.Error is not null)
        {
            var detail = result.ErrorDescription is not null ? $" ({result.ErrorDescription})" : "";
            var hint = result.Error == "redirect_uri_mismatch"
                ? " — the redirect_uri must match exactly, including 127.0.0.1 vs localhost."
                : ".";
            throw new DownloadException(
                $"Nexus returned an OAuth error: {result.Error}{detail}. " +
                $"Check that NEXUS_OAUTH_CLIENT_ID and the redirect URI match what Nexus has registered{hint}",
                retryable: false);
        }

        if (string.IsNullOrEmpty(result.Code))
            throw new DownloadException(
                $"The OAuth redirect did not include a code. Query received: {result.RawQuery ?? "(none)"}",
                retryable: false);

        if (result.State != state)
            throw new DownloadException("OAuth state mismatch — possible CSRF. Aborting sign-in.", retryable: false);

        var set = await ExchangeCodeAsync(result.Code!, redirectUri, verifier, clientId, http);
        NexusTokenStore.Save(set);

        var user = NexusJwt.DecodeAccessToken(set.AccessToken)
            ?? throw new DownloadException("Could not read the signed-in user from the Nexus token.", retryable: false);

        log?.Invoke($"Signed in as {user.Name} (id {user.UserId}).");
        return user;
    }

    public static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            throw new DownloadException($"Could not open a browser to {url}. Open it manually. ({ex.Message})", retryable: false);
        }
    }

    private static NexusTokenSet ParseTokenSet(JsonElement root)
    {
        var access = root.GetProperty("access_token").GetString()
            ?? throw new DownloadException("Nexus token response was missing access_token.", retryable: false);
        var refresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        var expiresIn = root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;

        return new NexusTokenSet(access, refresh, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
    }

    private static byte[] RandomBytes(int count)
    {
        var bytes = new byte[count];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return bytes;
    }
}
