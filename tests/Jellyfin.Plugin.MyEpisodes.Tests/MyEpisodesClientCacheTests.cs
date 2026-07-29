using System.Net.Http;
using System.Threading.Tasks;
using Jellyfin.Plugin.MyEpisodes.Tests.Utils;
using Moq;
using Moq.Protected;
using Xunit;

namespace Jellyfin.Plugin.MyEpisodes.Tests;

public class MyEpisodesClientCacheTests
{
    [Fact]
    public async Task FindShowIdAsync_PopulatesCacheWhenEmpty_AndUsesCache()
    {
        var myShowsHtml = """
            <html>
                <body>
                    <a href="/epsbyshow/200/Doctor Who">Doctor Who</a>
                </body>
            </html>
        """;
        var builder = new MyEpisodesClientTestBuilder()
            .WithMyShowsListResponse(myShowsHtml);
        var (client, handlerMock) = builder.Build();

        var showId = await client.FindOrAddShowAsync("Doctor Who", null);
        Assert.Equal(200, showId);
        // Verify that a GET to /myshows/list/ was made
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get && req.RequestUri.PathAndQuery.Contains("/myshows/list/")),
            ItExpr.IsAny<System.Threading.CancellationToken>());
        // Verify that no /search/ request was issued because the show was found in cache
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Post && req.RequestUri.PathAndQuery.Contains("/search/")),
            ItExpr.IsAny<System.Threading.CancellationToken>());
    }

    [Fact]
    public async Task FindShowIdAsync_UsesCacheOnSubsequentCalls_WithoutAdditionalNetwork()
    {
        var myShowsHtml = """
            <html>
                <body>
                    <a href="/epsbyshow/200/Doctor Who">Doctor Who</a>
                </body>
            </html>
        """;
        var builder = new MyEpisodesClientTestBuilder()
            .WithMyShowsListResponse(myShowsHtml);
        var (client, handlerMock) = builder.Build();

        // First call populates cache
        var firstId = await client.FindOrAddShowAsync("Doctor Who", null);
        Assert.Equal(200, firstId);
        // Reset mock invocation count
        handlerMock.Invocations.Clear();

        // Second call should hit cache only
        var secondId = await client.FindOrAddShowAsync("Doctor Who", null);
        Assert.Equal(200, secondId);
        // No network calls should happen on second call
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<System.Threading.CancellationToken>());
    }
}
