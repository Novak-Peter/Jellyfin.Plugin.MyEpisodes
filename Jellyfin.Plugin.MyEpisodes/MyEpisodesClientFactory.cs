using Microsoft.Extensions.Logging;
using System.Net;

namespace Jellyfin.Plugin.MyEpisodes;

public interface IMyEpisodesClientFactory
{
    MyEpisodesClient CreateClient(string username, string password);
}

public class MyEpisodesClientFactory : IMyEpisodesClientFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public MyEpisodesClientFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public MyEpisodesClient CreateClient(string username, string password)
    {
        var logger = _loggerFactory.CreateLogger<MyEpisodesClient>();

        var handler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        var httpClient = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = new Uri("https://www.myepisodes.com")
        };

        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        httpClient.DefaultRequestHeaders.Accept.ParseAdd(
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        httpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

        return new MyEpisodesClient(username, password, httpClient, logger);
    }
}