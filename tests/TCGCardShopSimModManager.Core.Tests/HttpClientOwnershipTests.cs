using System.Net;
using TCGCardShopSimModManager.Core;

namespace TCGCardShopSimModManager.Core.Tests;

public sealed class HttpClientOwnershipTests
{
    [Fact]
    public void Services_DoNotDisposeInjectedHttpClient()
    {
        var handler = new TrackingHandler();
        using var http = new HttpClient(handler);

        new ModpackIndexReader(http).Dispose();
        new UpdateChecker("owner/repo", "1.0.0", http).Dispose();
        new NexusApi("https://example.test/v1", "game", "test", http).Dispose();
        new NexusModSource(
            "https://example.test/v1", "game", NexusAuth.FromApiKey("key"), http).Dispose();
        new ModpackModSource("game", new LocalFileSource("."), http: http).Dispose();

        Assert.False(handler.WasDisposed);
    }

    private sealed class TrackingHandler : HttpMessageHandler
    {
        public bool WasDisposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }
}
