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
    public async Task EnsureLoggedInAsync_ReturnsTrue_WhenLoginSucceeds()
    {
        // Arrange
        var (client, handlerMock) = new MyEpisodesClientTestBuilder().Build();

        // Act
        var result = await client.EnsureLoggedInAsync();

        // Assert
        Assert.True(result);
        handlerMock.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Post && req.RequestUri.PathAndQuery.Contains("/login/")),
            ItExpr.IsAny<System.Threading.CancellationToken>());
    }

    [Fact]
    public async Task FindShowIdAsync_WithDuplicateShowsAndYear_ReturnsCorrectShowId()
    {
        // Arrange
        var searchHtml = """
                         <html>
                             <body>
                                 <a href="/epsbyshow/102/Doctor Who (2005)">Doctor Who (2005)</a>
                                 <a href="/epsbyshow/103/Doctor Who (1963)">Doctor Who (1963)</a>
                             </body>
                         </html>
                         """;

        var (client, _) = new MyEpisodesClientTestBuilder()
            .WithSearchResponse(searchHtml)
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
        var searchHtml = """
                         <html>
                             <body>
                                 <a href="/epsbyshow/101/Doctor Who">Doctor Who</a>
                                 <a href="/epsbyshow/102/Doctor Who (2005)">Doctor Who (2005)</a>
                             </body>
                         </html>
                         """;

        var (client, _) = new MyEpisodesClientTestBuilder()
            .WithSearchResponse(searchHtml)
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
        var searchHtml = """
                         <html>
                             <body>
                                 <a href="/epsbyshow/103/Doctor Who (2005)">Doctor Who (2005)</a>
                                 <a href="/epsbyshow/101/Doctor Who">Doctor Who</a>
                                 <a href="/epsbyshow/102/Doctor Who">Doctor Who</a>
                             </body>
                         </html>
                         """;

        var (client, _) = new MyEpisodesClientTestBuilder()
            .WithSearchResponse(searchHtml)
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
        var searchHtml = """
                         <html>
                             <body>
                                 <a href="/epsbyshow/103/Doctor Who (2005)">Doctor Who (2005)</a>
                                 <a href="/epsbyshow/101/Doctor Who">Doctor Who</a>
                                 <a href="/epsbyshow/102/Doctor Who">Doctor Who</a>
                             </body>
                         </html>
                         """;

        var (client, _) = new MyEpisodesClientTestBuilder()
            .WithSearchResponse(searchHtml)
            .Build();

        // Act
        var showId = await client.FindOrAddShowAsync("Doctor Who", 1963);

        // Assert
        Assert.Equal(101, showId);
    }
}