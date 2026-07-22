using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;
using Jellyfin.Plugin.MyEpisodes;

namespace Jellyfin.Plugin.MyEpisodes.Tests
{
    public class MyEpisodesClientTestBuilder
    {
        private string _searchHtml = "<html></html>";
        private string _loginHtml = "<html><body>Welcome testuser!</body></html>";

        public MyEpisodesClientTestBuilder WithSearchResponse(string html)
        {
            _searchHtml = html;
            return this;
        }

        public MyEpisodesClientTestBuilder WithLoginResponse(string html)
        {
            _loginHtml = html;
            return this;
        }

        public (MyEpisodesClient Client, Mock<HttpMessageHandler> HandlerMock) Build()
        {
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

            handlerMock
               .Protected()
               .Setup<Task<HttpResponseMessage>>("SendAsync",
                   ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Post && req.RequestUri.PathAndQuery.Contains("/login/")),
                   ItExpr.IsAny<System.Threading.CancellationToken>())
               .ReturnsAsync(new HttpResponseMessage
               {
                   StatusCode = HttpStatusCode.OK,
                   Content = new StringContent(_loginHtml)
               });

            handlerMock
               .Protected()
               .Setup<Task<HttpResponseMessage>>("SendAsync",
                   ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Post && req.RequestUri.PathAndQuery.Contains("/search/")),
                   ItExpr.IsAny<System.Threading.CancellationToken>())
               .ReturnsAsync(new HttpResponseMessage
               {
                   StatusCode = HttpStatusCode.OK,
                   Content = new StringContent(_searchHtml)
               });

            handlerMock
               .Protected()
               .Setup<Task<HttpResponseMessage>>("SendAsync",
                   ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Post && req.RequestUri.PathAndQuery.Contains("show_manage")),
                   ItExpr.IsAny<System.Threading.CancellationToken>())
               .ReturnsAsync(new HttpResponseMessage
               {
                   StatusCode = HttpStatusCode.OK,
                   Content = new StringContent("ok")
               });

            handlerMock
               .Protected()
               .Setup<Task<HttpResponseMessage>>("SendAsync",
                   ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get && req.RequestUri.PathAndQuery.Contains("/myshows/list/")),
                   ItExpr.IsAny<System.Threading.CancellationToken>())
               .ReturnsAsync(new HttpResponseMessage
               {
                   StatusCode = HttpStatusCode.OK,
                   Content = new StringContent("<html></html>")
               });

            var httpClient = new HttpClient(handlerMock.Object)
            {
                BaseAddress = new Uri("https://www.myepisodes.com")
            };
            var loggerMock = new Mock<ILogger>();
            var client = new MyEpisodesClient("testuser", "testpass", loggerMock.Object);

            typeof(MyEpisodesClient).GetField("_httpClient", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(client, httpClient);

            return (client, handlerMock);
        }
    }

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
            var searchHtml = @"
                <html>
                    <body>
                        <a href='/show/102/'>Doctor Who (2005)</a>
                        <a href='/show/103/'>Doctor Who (1963)</a>
                    </body>
                </html>";

            var (client, _) = new MyEpisodesClientTestBuilder()
                .WithSearchResponse(searchHtml)
                .Build();

            typeof(MyEpisodesClient).GetField("_isLoggedIn", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(client, true);

            // Act
            var showId = await client.FindShowIdAsync("Doctor Who", 2005);

            // Assert
            Assert.Equal(102, showId);
        }

        [Fact]
        public async Task FindShowIdAsync_WithExactMatchOnly_ReturnsExactMatch()
        {
            // Arrange
            var searchHtml = @"
                <html>
                    <body>
                        <a href='/show/101/'>Doctor Who</a>
                        <a href='/show/102/'>Doctor Who (2005)</a>
                    </body>
                </html>";

            var (client, _) = new MyEpisodesClientTestBuilder()
                .WithSearchResponse(searchHtml)
                .Build();

            typeof(MyEpisodesClient).GetField("_isLoggedIn", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(client, true);

            // Act
            var showId = await client.FindShowIdAsync("Doctor Who", 2005);

            // Assert
            Assert.Equal(101, showId);
        }

        [Fact]
        public async Task FindShowIdAsync_WithDuplicateBaseNamesAndYear2005_ReturnsYearMatch()
        {
            // Arrange
            var searchHtml = @"
                <html>
                    <body>
                        <a href='/show/103/'>Doctor Who (2005)</a>
                        <a href='/show/101/'>Doctor Who</a>
                        <a href='/show/102/'>Doctor Who</a>
                    </body>
                </html>";

            var (client, _) = new MyEpisodesClientTestBuilder()
                .WithSearchResponse(searchHtml)
                .Build();

            typeof(MyEpisodesClient).GetField("_isLoggedIn", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(client, true);

            // Act
            var showId = await client.FindShowIdAsync("Doctor Who", 2005);

            // Assert
            Assert.Equal(103, showId);
        }

        [Fact]
        public async Task FindShowIdAsync_WithDuplicateBaseNamesAndYear1963_ReturnsFirstBaseNameFallback()
        {
            // Arrange
            var searchHtml = @"
                <html>
                    <body>
                        <a href='/show/103/'>Doctor Who (2005)</a>
                        <a href='/show/101/'>Doctor Who</a>
                        <a href='/show/102/'>Doctor Who</a>
                    </body>
                </html>";

            var (client, _) = new MyEpisodesClientTestBuilder()
                .WithSearchResponse(searchHtml)
                .Build();

            typeof(MyEpisodesClient).GetField("_isLoggedIn", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(client, true);

            // Act
            var showId = await client.FindShowIdAsync("Doctor Who", 1963);

            // Assert
            Assert.Equal(101, showId);
        }
    }
}
