using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TCGCardShopSimModManager.Core;

namespace TCGCardShopSimModManager.Core.Tests;

public sealed class NexusOAuthTests
{
    [Fact]
    public void PkceVerifier_IsLongEnough_AndChallengeMatchesS256()
    {
        var verifier = NexusOAuth.GenerateCodeVerifier();
        Assert.True(verifier.Length >= 43, "PKCE verifier must be >= 43 chars");

        var challenge = NexusOAuth.ComputeCodeChallenge(verifier);
        using var sha = SHA256.Create();
        var expected = Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(expected, challenge);
    }

    [Fact]
    public void BuildAuthorizeUrl_ContainsRequiredParameters()
    {
        var url = NexusOAuth.BuildAuthorizeUrl(
            "http://127.0.0.1:8089/callback", "mystate", "mychallenge", "public_test");

        Assert.StartsWith(NexusOAuth.AuthorizeEndpoint, url);
        Assert.Contains("client_id=public_test", url);
        Assert.Contains("response_type=code", url);
        Assert.Contains("scope=", url);
        Assert.Contains("redirect_uri=", url);
        Assert.Contains("state=mystate", url);
        Assert.Contains("code_challenge_method=S256", url);
        Assert.Contains("code_challenge=mychallenge", url);
    }

    [Fact]
    public void DecodeAccessToken_ReadsUserAndPremium()
    {
        var header = B64Url("{\"alg\":\"RS256\",\"typ\":\"JWT\"}");
        var payload = B64Url(
            "{\"sub\":\"12345\",\"user\":{\"username\":\"TestAccount\"," +
            "\"membership_roles\":[\"member\",\"premium\"]},\"exp\":1754411198}");
        var jwt = $"{header}.{payload}.signature";

        var user = NexusJwt.DecodeAccessToken(jwt);

        Assert.NotNull(user);
        Assert.Equal(12345, user!.UserId);
        Assert.Equal("TestAccount", user.Name);
        Assert.True(user.IsPremium);
    }

    [Fact]
    public void DecodeAccessToken_FreeAccount_NotPremium()
    {
        var payload = B64Url(
            "{\"sub\":\"99\",\"user\":{\"username\":\"FreeUser\",\"membership_roles\":[\"member\"]}}");
        var jwt = $"{B64Url("{}")}.{payload}.sig";

        var user = NexusJwt.DecodeAccessToken(jwt);

        Assert.NotNull(user);
        Assert.Equal("FreeUser", user!.Name);
        Assert.False(user.IsPremium);
    }

    [Fact]
    public void NexusTokenStore_RoundTripAndDelete()
    {
        try
        {
            var set = new NexusTokenSet("access-token", "refresh-token", DateTimeOffset.UtcNow.AddHours(1));
            NexusTokenStore.Save(set);
            Assert.True(NexusTokenStore.Exists);

            var loaded = NexusTokenStore.TryLoad();
            Assert.NotNull(loaded);
            Assert.Equal("access-token", loaded!.AccessToken);
            Assert.Equal("refresh-token", loaded.RefreshToken);

            NexusTokenStore.Delete();
            Assert.False(NexusTokenStore.Exists);
            Assert.Null(NexusTokenStore.TryLoad());
        }
        finally
        {
            NexusTokenStore.Delete();
        }
    }

    [Fact]
    public async Task LoopbackListener_CapturesCodeAndState()
    {
        int port;
        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
        }

        var uri = $"http://127.0.0.1:{port}/callback";
        await using var listener = new LoopbackOAuthListener(uri);
        await listener.StartAsync();

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        using var ns = client.GetStream();
        var request = Encoding.ASCII.GetBytes("GET /callback?code=ABC123&state=ST456 HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n");
        await ns.WriteAsync(request, CancellationToken.None);

        var (code, state) = await listener.WaitForCallbackAsync(CancellationToken.None);

        Assert.Equal("ABC123", code);
        Assert.Equal("ST456", state);
    }

    private static string B64Url(string s) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
