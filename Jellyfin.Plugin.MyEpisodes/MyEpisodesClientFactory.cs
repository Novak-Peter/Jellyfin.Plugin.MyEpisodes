using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MyEpisodes;

public interface IMyEpisodesClientFactory
{
    MyEpisodesClient CreateClient(string apiKey);
}

public class MyEpisodesClientFactory : IMyEpisodesClientFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public MyEpisodesClientFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public MyEpisodesClient CreateClient(string apiKey)
    {
        var logger = _loggerFactory.CreateLogger<MyEpisodesClient>();

        var httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://api.myepisodes.com")
        };

        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");

        return new MyEpisodesClient(apiKey, httpClient, logger);
    }
}