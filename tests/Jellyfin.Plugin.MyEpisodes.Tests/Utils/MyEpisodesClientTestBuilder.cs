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
    private string _searchHtml = "<html></html>";
    private string _loginHtml = "<html><body><a href=\"/logout/\"><strong>testuser</strong> (Logout)</a></body></html>";
    private string _myShowsListHtml = "<html></html>";
    private string _epsUpdateResponseContent = "ok";

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

    public MyEpisodesClientTestBuilder WithMyShowsListResponse(string html)
    {
        _myShowsListHtml = html;
        return this;
    }

    public MyEpisodesClientTestBuilder WithEpsUpdateResponse(string content)
    {
        _epsUpdateResponseContent = content;
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
                Content = new StringContent(_myShowsListHtml)
            });

        // eps_update stub
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Post && req.RequestUri.PathAndQuery.Contains("/eps_update")),
                ItExpr.IsAny<System.Threading.CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(_epsUpdateResponseContent)
            });

        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://www.myepisodes.com")
        };
        var loggerMock = new Mock<ILogger>();
        var client = new MyEpisodesClient("testuser", "testpass", httpClient, loggerMock.Object);

        return (client, handlerMock);
    }
}
