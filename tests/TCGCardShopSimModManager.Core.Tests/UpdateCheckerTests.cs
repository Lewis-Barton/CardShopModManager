using System.Net;
using System.Text;
using TCGCardShopSimModManager.Core;

namespace TCGCardShopSimModManager.Core.Tests;

public sealed class UpdateCheckerTests
{
    [Theory]
    [InlineData("0.3.0.41", "v0.3.0.42", false)]
    [InlineData("0.3.0.42", "v0.3.0.42", true)]
    [InlineData("0.3.0.43", "v0.3.0.42", true)]
    public async Task CheckAsync_UsesReleaseBuildComponent(
        string localVersion, string releaseTag, bool expectedUpToDate)
    {
        using var http = new HttpClient(new ReleaseHandler(releaseTag));
        using var checker = new UpdateChecker("owner/repo", localVersion, http);

        var result = await checker.CheckAsync(CancellationToken.None);

        Assert.True(result.HasRelease);
        Assert.Equal(expectedUpToDate, result.IsUpToDate);
    }

    private sealed class ReleaseHandler(string tag) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $"{{\"tag_name\":\"{tag}\",\"html_url\":\"https://example.test/release\"}}",
                    Encoding.UTF8,
                    "application/json")
            });
    }
}
