using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Jellyfin.Plugin.MyEpisodes.Tests.Utils;
using Moq;
using Moq.Protected;
using Xunit;

namespace Jellyfin.Plugin.MyEpisodes.Tests;

public class MyEpisodesClientTests
{
    [Fact]
    public async Task PopulateShowsAsync_SendsGetRequestToApi()
    {
        // Arrange
        var jsonResponse = """
            {
                "data": [
                    { "showid": 100, "showname": "Doctor Who" }
                ]
            }
            """;
        var (client, handlerMock) = new MyEpisodesClientTestBuilder()
            .WithMyShowsListResponse(jsonResponse)
            .Build();

        // Act
        await client.PopulateShowsAsync();

        // Assert
        handlerMock.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get && req.RequestUri != null && req.RequestUri.PathAndQuery.Contains("/v1/me/shows")),
            ItExpr.IsAny<System.Threading.CancellationToken>());
    }

    [Fact]
    public async Task FindShowIdAsync_WithDuplicateShowsAndYear_ReturnsCorrectShowId()
    {
        // Arrange
        var searchJson = """
            {
                "data": [
                    { "showid": 102, "showname": "Doctor Who (2005)" },
                    { "showid": 103, "showname": "Doctor Who (1963)" }
                ]
            }
            """;

        var (client, _) = new MyEpisodesClientTestBuilder()
            .WithSearchResponse(searchJson)
            .Build();

        // Act
        var showId = await client.FindOrAddShowAsync("Doctor Who", 2005);

        // Assert
        Assert.Equal(102, showId);
    }

    [Fact]
    public async Task FindShowIdAsync_WithExactMatchOnly_ReturnsExactMatch()
    {
        // Arrange
        var searchJson = """
            {
                "data": [
                    { "showid": 101, "showname": "Doctor Who" },
                    { "showid": 102, "showname": "Doctor Who (2005)" }
                ]
            }
            """;

        var (client, _) = new MyEpisodesClientTestBuilder()
            .WithSearchResponse(searchJson)
            .Build();

        // Act
        var showId = await client.FindOrAddShowAsync("Doctor Who", 2005);

        // Assert
        Assert.Equal(101, showId);
    }

    [Fact]
    public async Task FindShowIdAsync_WithDuplicateBaseNamesAndYear2005_ReturnsYearMatch()
    {
        // Arrange
        var searchJson = """
            {
                "data": [
                    { "showid": 103, "showname": "Doctor Who (2005)" },
                    { "showid": 101, "showname": "Doctor Who" },
                    { "showid": 102, "showname": "Doctor Who" }
                ]
            }
            """;

        var (client, _) = new MyEpisodesClientTestBuilder()
            .WithSearchResponse(searchJson)
            .Build();

        // Act
        var showId = await client.FindOrAddShowAsync("Doctor Who", 2005);

        // Assert
        Assert.Equal(103, showId);
    }

    [Fact]
    public async Task FindShowIdAsync_WithDuplicateBaseNamesAndYear1963_ReturnsFirstBaseNameFallback()
    {
        // Arrange
        var searchJson = """
            {
                "data": [
                    { "showid": 103, "showname": "Doctor Who (2005)" },
                    { "showid": 101, "showname": "Doctor Who" },
                    { "showid": 102, "showname": "Doctor Who" }
                ]
            }
            """;

        var (client, _) = new MyEpisodesClientTestBuilder()
            .WithSearchResponse(searchJson)
            .Build();

        // Act
        var showId = await client.FindOrAddShowAsync("Doctor Who", 1963);

        // Assert
        Assert.Equal(101, showId);
    }

    [Fact]
    public async Task FindShowIdAsync_NullOrEmptyShowName_ReturnsNullWithoutNetwork_MED_33()
    {
        // Arrange
        var (client, handlerMock) = new MyEpisodesClientTestBuilder().Build();

        // Act
        var nullResult = await client.FindOrAddShowAsync(null!, 2020);
        var emptyResult = await client.FindOrAddShowAsync("", 2020);

        // Assert
        Assert.Null(nullResult);
        Assert.Null(emptyResult);
        handlerMock.Protected().Verify("SendAsync", Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<System.Threading.CancellationToken>());
    }

    [Fact]
    public async Task FindShowIdAsync_SearchReturnsZeroResults_ReturnsNull_MED_25()
    {
        // Arrange
        var emptySearchJson = """{"data": []}""";
        var (client, _) = new MyEpisodesClientTestBuilder()
            .WithSearchResponse(emptySearchJson)
            .Build();

        // Act
        var result = await client.FindOrAddShowAsync("NonExistentShow123", null);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateEpisodeStatus_WatchedTrue_SendsPutWithWatchedTrue_HIGH_42()
    {
        // Arrange
        var (client, handlerMock) = new MyEpisodesClientTestBuilder().Build();

        // Act
        var success = await client.UpdateEpisodeStatus(100, 1, 2, EpisodeStatus.Watched);

        // Assert
        Assert.True(success);
        handlerMock.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Put &&
                req.RequestUri != null &&
                req.RequestUri.PathAndQuery.Contains("/v1/me/episodes/100/1/2") &&
                req.Content != null &&
                req.Content.ReadAsStringAsync().Result.Contains("\"watched\":true")),
            ItExpr.IsAny<System.Threading.CancellationToken>());
    }

    [Fact]
    public async Task UpdateEpisodeStatus_WatchedFalse_SendsPutWithWatchedFalse_HIGH_43()
    {
        // Arrange
        var (client, handlerMock) = new MyEpisodesClientTestBuilder().Build();

        // Act
        var success = await client.UpdateEpisodeStatus(100, 1, 2, EpisodeStatus.Unwatched);

        // Assert
        Assert.True(success);
        handlerMock.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Put &&
                req.RequestUri != null &&
                req.RequestUri.PathAndQuery.Contains("/v1/me/episodes/100/1/2") &&
                req.Content != null &&
                req.Content.ReadAsStringAsync().Result.Contains("\"watched\":false")),
            ItExpr.IsAny<System.Threading.CancellationToken>());
    }

    [Fact]
    public async Task UpdateEpisodeStatus_AcquiredTrue_SendsPutWithAcquiredTrue_HIGH_226()
    {
        // Arrange
        var (client, handlerMock) = new MyEpisodesClientTestBuilder().Build();

        // Act
        var success = await client.UpdateEpisodeStatus(100, 1, 2, EpisodeStatus.Acquired);

        // Assert
        Assert.True(success);
        handlerMock.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Put &&
                req.RequestUri != null &&
                req.RequestUri.PathAndQuery.Contains("/v1/me/episodes/100/1/2") &&
                req.Content != null &&
                req.Content.ReadAsStringAsync().Result.Contains("\"acquired\":true")),
            ItExpr.IsAny<System.Threading.CancellationToken>());
    }

    [Fact]
    public async Task UpdateEpisodeStatus_AcquiredFalse_SendsPutWithAcquiredFalse_HIGH_227()
    {
        // Arrange
        var (client, handlerMock) = new MyEpisodesClientTestBuilder().Build();

        // Act
        var success = await client.UpdateEpisodeStatus(100, 1, 2, EpisodeStatus.Unacquired);

        // Assert
        Assert.True(success);
        handlerMock.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Put &&
                req.RequestUri != null &&
                req.RequestUri.PathAndQuery.Contains("/v1/me/episodes/100/1/2") &&
                req.Content != null &&
                req.Content.ReadAsStringAsync().Result.Contains("\"acquired\":false")),
            ItExpr.IsAny<System.Threading.CancellationToken>());
    }

    [Fact]
    public async Task UpdateEpisodeStatus_Non2xxStatus_ReturnsFalse_MED_46()
    {
        // Arrange
        var (client, handlerMock) = new MyEpisodesClientTestBuilder()
            .WithEpisodeUpdateResponse("error")
            .Build();

        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Put && req.RequestUri != null && req.RequestUri.PathAndQuery.Contains("/v1/me/episodes/")),
                ItExpr.IsAny<System.Threading.CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.InternalServerError
            });

        // Act
        var result = await client.UpdateEpisodeStatus(100, 1, 1, EpisodeStatus.Watched);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task UpdateEpisodesBulkAsync_Over500Items_ChunksIntoMultipleRequests_MED()
    {
        // Arrange
        var (client, handlerMock) = new MyEpisodesClientTestBuilder().Build();
        var items = new List<EpisodeUpdateItemDto>();
        for (int i = 1; i <= 501; i++)
        {
            items.Add(new EpisodeUpdateItemDto { ShowId = 1, Season = 1, Episode = i, Watched = true });
        }

        // Act
        var result = await client.UpdateEpisodesBulkAsync(items);

        // Assert
        Assert.True(result);
        handlerMock.Protected().Verify("SendAsync", Times.Exactly(2),
            ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Post && req.RequestUri != null && req.RequestUri.PathAndQuery.Equals("/v1/me/episodes")),
            ItExpr.IsAny<System.Threading.CancellationToken>());
    }
}