using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.MyEpisodes;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHttpClient("MyEpisodes", client =>
        {
            client.BaseAddress = new System.Uri("https://www.myepisodes.com");
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        }).ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.HttpClientHandler
        {
            UseCookies = false,
            AllowAutoRedirect = true
        });
        serviceCollection.AddSingleton<IMyEpisodesClientFactory, MyEpisodesClientFactory>();
        serviceCollection.AddHostedService<MyEpisodesTracker>();
    }
}