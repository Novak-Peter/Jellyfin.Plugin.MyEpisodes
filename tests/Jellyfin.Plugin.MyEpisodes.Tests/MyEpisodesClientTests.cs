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
}