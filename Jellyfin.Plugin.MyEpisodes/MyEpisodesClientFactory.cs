using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MyEpisodes;

public interface IMyEpisodesClientFactory
{
    MyEpisodesClient CreateClient(string username, string password);
}

public class MyEpisodesClientFactory : IMyEpisodesClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;

    public MyEpisodesClientFactory(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    {
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
    }

    public MyEpisodesClient CreateClient(string username, string password)
    {
        var httpClient = _httpClientFactory.CreateClient("MyEpisodes");
        var logger = _loggerFactory.CreateLogger<MyEpisodesClient>();
        return new MyEpisodesClient(username, password, httpClient, logger);
    }
}