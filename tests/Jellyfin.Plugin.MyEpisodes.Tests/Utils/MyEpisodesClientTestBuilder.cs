using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;

namespace Jellyfin.Plugin.MyEpisodes.Tests.Utils;

public class MyEpisodesClientTestBuilder
{
    private string _searchJsonResponse = """{"data": []}""";
    private string _myShowsListJsonResponse = """{"data": []}""";
    private string _episodeUpdateResponseContent = "{}";
    private string _apiKey = "myeps_testkey123";

    public MyEpisodesClientTestBuilder WithApiKey(string apiKey)
    {
        _apiKey = apiKey;
        return this;
    }

    public MyEpisodesClientTestBuilder WithSearchResponse(string json)
    {
        _searchJsonResponse = json;
        return this;
    }

    public MyEpisodesClientTestBuilder WithMyShowsListResponse(string json)
    {
        _myShowsListJsonResponse = json;
        return this;
    }

    public MyEpisodesClientTestBuilder WithEpisodeUpdateResponse(string content)
    {
        _episodeUpdateResponseContent = content;
        return this;
    }

    public (MyEpisodesClient Client, Mock<HttpMessageHandler> HandlerMock) Build()
    {
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

        handlerMock
            .Protected()
            .Setup("Dispose", ItExpr.IsAny<bool>());

        // GET /v1/shows?search=...
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get && req.RequestUri != null && req.RequestUri.PathAndQuery.Contains("/v1/shows")),
                ItExpr.IsAny<System.Threading.CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(_searchJsonResponse, System.Text.Encoding.UTF8, "application/json")
            });

        // PUT /v1/me/shows/{id}
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Put && req.RequestUri != null && req.RequestUri.PathAndQuery.Contains("/v1/me/shows/")),
                ItExpr.IsAny<System.Threading.CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
            });

        // GET /v1/me/shows
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Get && req.RequestUri != null && req.RequestUri.PathAndQuery.Contains("/v1/me/shows")),
                ItExpr.IsAny<System.Threading.CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(_myShowsListJsonResponse, System.Text.Encoding.UTF8, "application/json")
            });

        // PUT /v1/me/episodes/{showid}/{season}/{episode}
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Put && req.RequestUri != null && req.RequestUri.PathAndQuery.Contains("/v1/me/episodes/")),
                ItExpr.IsAny<System.Threading.CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(_episodeUpdateResponseContent, System.Text.Encoding.UTF8, "application/json")
            });

        // POST /v1/me/episodes (Bulk)
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Post && req.RequestUri != null && req.RequestUri.PathAndQuery.Equals("/v1/me/episodes", StringComparison.OrdinalIgnoreCase)),
                ItExpr.IsAny<System.Threading.CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(_episodeUpdateResponseContent, System.Text.Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://api.myepisodes.com")
        };
        var loggerMock = new Mock<ILogger>();
        var client = new MyEpisodesClient(_apiKey, httpClient, loggerMock.Object);

        return (client, handlerMock);
    }
}
