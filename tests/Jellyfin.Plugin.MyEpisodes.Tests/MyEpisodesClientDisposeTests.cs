using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Jellyfin.Plugin.MyEpisodes.Tests.Utils;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.MyEpisodes.Tests;

public class MyEpisodesClientDisposeTests
{
    [Fact]
    public void Dispose_DisposesUnderlyingHttpClient()
    {
        var (_, _) = new MyEpisodesClientTestBuilder().Build();
        var trackingHandler = new TrackingHandler();
        var httpClient = new HttpClient(trackingHandler) { BaseAddress = new Uri("https://www.myepisodes.com") };
        var clientWithTracking = new MyEpisodesClient("testuser", "testpass", httpClient, Mock.Of<Microsoft.Extensions.Logging.ILogger>());
        clientWithTracking.Dispose();
        Assert.True(trackingHandler.IsDisposed);
    }

    [Fact]
    public void Dispose_CalledTwice_IsIdempotent()
    {
        var (_, _) = new MyEpisodesClientTestBuilder().Build();
        var trackingHandler = new TrackingHandler();
        var httpClient = new HttpClient(trackingHandler) { BaseAddress = new Uri("https://www.myepisodes.com") };
        var clientWithTracking = new MyEpisodesClient("testuser", "testpass", httpClient, Mock.Of<Microsoft.Extensions.Logging.ILogger>());
        clientWithTracking.Dispose();
        // second dispose should not throw
        var ex = Record.Exception(() => clientWithTracking.Dispose());
        Assert.Null(ex);
    }
}

// Helper handler to detect disposal
public class TrackingHandler : HttpMessageHandler
{
    public bool IsDisposed { get; private set; }

    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        base.Dispose(disposing);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
