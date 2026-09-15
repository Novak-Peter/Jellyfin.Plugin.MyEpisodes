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
        var myShowsJson = """
            {
                "data": [
                    { "showid": 200, "showname": "Doctor Who" }
                ]
            }
        """;
        var builder = new MyEpisodesClientTestBuilder()
            .WithMyShowsListResponse(myShowsJson);
        var (client, handlerMock) = builder.Build();

        var showId = await client.FindOrAddShowAsync("Doctor Who", null);
        Assert.Equal(200, showId);
        // Verify that a GET to /v1/me/shows was made
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get && req.RequestUri != null && req.RequestUri.PathAndQuery.Contains("/v1/me/shows")),
            ItExpr.IsAny<System.Threading.CancellationToken>());
        // Verify that no /v1/shows?search= request was issued because the show was found in cache
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get && req.RequestUri != null && req.RequestUri.PathAndQuery.Contains("/v1/shows?search=")),
            ItExpr.IsAny<System.Threading.CancellationToken>());
    }

    [Fact]
    public async Task FindShowIdAsync_UsesCacheOnSubsequentCalls_WithoutAdditionalNetwork()
    {
        var myShowsJson = """
            {
                "data": [
                    { "showid": 200, "showname": "Doctor Who" }
                ]
            }
        """;
        var builder = new MyEpisodesClientTestBuilder()
            .WithMyShowsListResponse(myShowsJson);
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

    [Fact]
    public async Task PopulateShowsAsync_ClearsCacheBeforeRefill_HIGH_9()
    {
        // Arrange
        var initialJson = """{"data": [{ "showid": 100, "showname": "Doctor Who" }]}""";
        var updatedJson = """{"data": [{ "showid": 200, "showname": "Sherlock" }]}""";

        var (client, handlerMock) = new MyEpisodesClientTestBuilder()
            .WithMyShowsListResponse(initialJson)
            .Build();

        await client.PopulateShowsAsync();
        var idDoctorWho = await client.FindOrAddShowAsync("Doctor Who", null);
        Assert.Equal(100, idDoctorWho);

        // Update response and refill
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get && req.RequestUri != null && req.RequestUri.PathAndQuery.Contains("/v1/me/shows")),
                ItExpr.IsAny<System.Threading.CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent(updatedJson, System.Text.Encoding.UTF8, "application/json")
            });

        // Act
        await client.PopulateShowsAsync();

        // Assert: Old show Doctor Who should no longer be in cache, new show Sherlock should be resolved
        var idSherlock = await client.FindOrAddShowAsync("Sherlock", null);
        Assert.Equal(200, idSherlock);
    }

    [Fact]
    public async Task AddShowAsync_ShowAlreadyInCache_ReturnsEarlyWithoutNetwork_HIGH_37()
    {
        // Arrange
        var initialJson = """{"data": [{ "showid": 200, "showname": "Doctor Who" }]}""";
        var (client, handlerMock) = new MyEpisodesClientTestBuilder()
            .WithMyShowsListResponse(initialJson)
            .Build();

        await client.PopulateShowsAsync();
        handlerMock.Invocations.Clear();

        // Act
        await client.AddShowAsync(200);

        // Assert: PUT /v1/me/shows/200 should NOT be called
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Put && req.RequestUri != null && req.RequestUri.PathAndQuery.Contains("/v1/me/shows/")),
            ItExpr.IsAny<System.Threading.CancellationToken>());
    }

    [Fact]
    public async Task AddShowAsync_ShowNotInCache_SendsPutRequestAndRefreshesCache_HIGH_38()
    {
        // Arrange
        var initialJson = """{"data": [{ "showid": 200, "showname": "Doctor Who" }]}""";
        var (client, handlerMock) = new MyEpisodesClientTestBuilder()
            .WithMyShowsListResponse(initialJson)
            .Build();

        await client.PopulateShowsAsync();
        handlerMock.Invocations.Clear();

        // Act
        await client.AddShowAsync(300);

        // Assert: PUT /v1/me/shows/300 was issued
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Put && req.RequestUri != null && req.RequestUri.PathAndQuery.Contains("/v1/me/shows/300")),
            ItExpr.IsAny<System.Threading.CancellationToken>());

        // Assert: PopulateShowsAsync was called after add to refresh cache
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get && req.RequestUri != null && req.RequestUri.PathAndQuery.Contains("/v1/me/shows")),
            ItExpr.IsAny<System.Threading.CancellationToken>());
    }
}
