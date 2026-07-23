using System.CommandLine;
using System.Text.Json;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Extensions;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Jellyfin.Plugin.MyEpisodes.TestHarness.Cli;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var configOption = new Option<string>("--config", "Configuration") { IsRequired = true };
        var rootCommand = new RootCommand("MyEpisodes test harness CLI") { configOption };

        rootCommand.SetHandler(async (configJson) =>
        {
            var trackingSignal = new TaskCompletionSource<TrackingCompletedEventArgs>();
            var pluginConfiguration = LoadPluginConfiguration(configJson);

            var builder = Host.CreateApplicationBuilder();
            builder.Services.AddSingleton<IServerApplicationHost, StubServerApplicationHost>();
            builder.Services.AddSingleton<IUserDataManager, StubUserDataManager>();

            var registrator = new PluginServiceRegistrator();
            var hostStub = builder.Services.BuildServiceProvider().GetRequiredService<IServerApplicationHost>();
            registrator.RegisterServices(builder.Services, hostStub);

            var host = builder.Build();

            await host.StartAsync();

            var tracker = host.Services.GetServices<IHostedService>()
                .OfType<MyEpisodesTracker>()
                .FirstOrDefault();

            if (tracker != null)
            {
                tracker.TrackingCompleted += (sender, args) =>
                {
                    trackingSignal.TrySetResult(args);
                };
            }

            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            var userDataManager = host.Services.GetRequiredService<IUserDataManager>();
            var targetUserId = pluginConfiguration.UserConfigurations.FirstOrDefault()?.JellyfinUserId;
            if (targetUserId is not null && Guid.TryParse(targetUserId, out var targetUserGuid))
            {
                var user = new User(targetUserId, "test", "test")
                {
                    Id = targetUserGuid
                };
                var fakeItem = new Episode()
                    { SeriesName = "The Night Manager", ParentIndexNumber = 2, IndexNumber = 1 };
                var fakeData = new UserItemData { Key = "", Played = true };
                userDataManager.SaveUserData(user, fakeItem, fakeData, UserDataSaveReason.PlaybackFinished, CancellationToken.None);
                await trackingSignal.Task.ConfigureAwait(false);
            }
            else
            { 
                logger.LogWarning("{TargetUserId} not found or not a GUID", targetUserId);
            }
            
            await host.StopAsync();
        }, configOption);

        return await rootCommand.InvokeAsync(args);
    }

    static PluginConfiguration LoadPluginConfiguration(string configJson)
    {
        var pluginConfiguration = JsonSerializer.Deserialize<PluginConfiguration>(configJson) 
            ?? throw new Exception("Plugin configuration could not be deserialized");

        var mockPaths = Substitute.For<IApplicationPaths>();
        mockPaths.PluginConfigurationsPath.Returns("C:\\DummyPath");

        var mockXml = Substitute.For<IXmlSerializer>();
        mockXml.DeserializeFromFile(typeof(PluginConfiguration), Arg.Any<string>()).Returns(pluginConfiguration);
        
        _ = new Plugin(mockPaths, mockXml);

        var configResolved = Plugin.Instance?.Configuration;
        return configResolved ?? throw new Exception("Plugin configuration could not be resolved through Plugin");
    }
}
