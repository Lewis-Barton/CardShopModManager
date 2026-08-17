using System.Net;
using System.Net.Sockets;
using System.Text;
using TCGCardShopSimModManager.Core;

namespace TCGCardShopSimModManager.Core.Tests;

public sealed class LocalHttpServerTests
{
    [Fact]
    public async Task FragmentedRequestHeader_IsParsed()
    {
        using var server = new LocalHttpServer
        {
            Provider = request => new HttpResponse(
                request.RangeStart == 3 ? 200 : 400,
                Encoding.ASCII.GetBytes(request.Path), null)
        };
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, server.Port);
        await using var stream = client.GetStream();

        foreach (var part in new[] { "GET /file", ".zip HTTP/1.1\r\nHost: localhost\r\nRan", "ge: bytes=3-\r\n\r\n" })
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes(part));
            await stream.FlushAsync();
        }

        using var reader = new StreamReader(stream, Encoding.ASCII);
        var response = await reader.ReadToEndAsync();

        Assert.StartsWith("HTTP/1.1 200", response);
        Assert.EndsWith("/file.zip", response);
    }

    [Fact]
    public async Task FolderProvider_StreamsRequestedRange()
    {
        var root = Path.Combine(Path.GetTempPath(), $"http-server-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var payload = Enumerable.Range(0, 2 * 1024 * 1024)
                .Select(i => (byte)(i % 251))
                .ToArray();
            await File.WriteAllBytesAsync(Path.Combine(root, "large.bin"), payload);

            using var server = new LocalHttpServer
            {
                Provider = LocalHttpServer.FolderProvider(root)
            };
            using var http = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, server.Url("large.bin"));
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(1_500_000, null);

            using var response = await http.SendAsync(request);
            var body = await response.Content.ReadAsByteArrayAsync();

            Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
            Assert.Equal(payload[1_500_000..], body);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Dispose_CancelsIncompleteClientRequest()
    {
        var server = new LocalHttpServer();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, server.Port);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\n"));

        server.Dispose();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        Assert.Equal(0, await stream.ReadAsync(new byte[1], timeout.Token));
    }
}
