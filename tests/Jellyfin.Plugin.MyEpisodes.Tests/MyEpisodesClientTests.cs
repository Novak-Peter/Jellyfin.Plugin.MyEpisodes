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
    public class MyEpisodesClientTests
    {
        [Fact]
        public async Task EnsureLoggedInAsync_ReturnsTrue_WhenLoginSucceeds()
        {
            // Arrange: mock HttpMessageHandler to return a page containing the username
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock
               .Protected()
               .Setup<Task<HttpResponseMessage>>("SendAsync",
                   ItExpr.IsAny<HttpRequestMessage>(),
                   ItExpr.IsAny<System.Threading.CancellationToken>())
               .ReturnsAsync(new HttpResponseMessage
               {
                   StatusCode = HttpStatusCode.OK,
                   Content = new StringContent("<html><body>Welcome testuser!</body></html>")
               })
               .Verifiable();

            var httpClient = new HttpClient(handlerMock.Object)
            {
                BaseAddress = new Uri("https://www.myepisodes.com")
            };
            var loggerMock = new Mock<ILogger>();
            var client = new MyEpisodesClient("testuser", "testpass", loggerMock.Object);
            // Inject mocked HttpClient via reflection (internal field _httpClient)
            var field = typeof(MyEpisodesClient).GetField("_httpClient", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            field.SetValue(client, httpClient);

            // Act
            var result = await client.EnsureLoggedInAsync();

            // Assert
            Assert.True(result);
            handlerMock.Protected().Verify("SendAsync", Times.Once(),
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Post && req.RequestUri.PathAndQuery.Contains("/login/")),
                ItExpr.IsAny<System.Threading.CancellationToken>());
        }
    }
}
