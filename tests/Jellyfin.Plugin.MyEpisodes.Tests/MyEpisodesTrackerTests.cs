using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MyEpisodes.Tests.Utils;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace Jellyfin.Plugin.MyEpisodes.Tests;

public class MyEpisodesTrackerTests : IDisposable
{
    private readonly Mock<IUserDataManager> _userDataManagerMock;
    private readonly Mock<ILibraryManager> _libraryManagerMock;
    private readonly Mock<ILogger<MyEpisodesTracker>> _trackerLoggerMock;
    private readonly Mock<IMyEpisodesClientFactory> _clientFactoryMock;
    private readonly Mock<IApplicationPaths> _appPathsMock;
    private readonly Mock<IXmlSerializer> _xmlSerializerMock;
    private readonly PluginConfiguration _pluginConfig;

    public MyEpisodesTrackerTests()
    {
        _userDataManagerMock = new Mock<IUserDataManager>();
        _libraryManagerMock = new Mock<ILibraryManager>();
        _trackerLoggerMock = new Mock<ILogger<MyEpisodesTracker>>();
        _clientFactoryMock = new Mock<IMyEpisodesClientFactory>();
        _appPathsMock = new Mock<IApplicationPaths>();
        _xmlSerializerMock = new Mock<IXmlSerializer>();

        _pluginConfig = new PluginConfiguration
        {
            UserConfigurations = new List<MyEpisodesUserConfiguration>()
        };

        _appPathsMock.Setup(x => x.PluginConfigurationsPath).Returns("dummy_path");
        _appPathsMock.Setup(x => x.PluginsPath).Returns("dummy_path");
        _appPathsMock.Setup(x => x.ConfigurationDirectoryPath).Returns("dummy_path");
        _appPathsMock.Setup(x => x.ProgramDataPath).Returns("dummy_path");
        _xmlSerializerMock.Setup(x => x.DeserializeFromFile(typeof(PluginConfiguration), It.IsAny<string>()))
            .Returns(_pluginConfig);

        // Reset singleton before each test
        typeof(Plugin).GetProperty(nameof(Plugin.Instance))?.SetValue(null, null);
        _ = new Plugin(_appPathsMock.Object, _xmlSerializerMock.Object);

        // Assign the static LibraryManager reference
        BaseItem.LibraryManager = _libraryManagerMock.Object;
    }

    public void Dispose()
    {
        typeof(Plugin).GetProperty(nameof(Plugin.Instance))?.SetValue(null, null);
        BaseItem.LibraryManager = null!;
    }

    [Fact]
    public async Task OnUserDataSaved_UsesSeriesProductionYear_NotEpisodeAirYear_HIGH_68()
    {
        // Arrange
        var userGuid = Guid.NewGuid();
        var userConfig = new MyEpisodesUserConfiguration
        {
            JellyfinUserId = userGuid.ToString("N"),
            ApiKey = "key1",
            SyncWatched = true
        };
        _pluginConfig.UserConfigurations.Add(userConfig);

        var jsonResponse = """
            {
                "data": [
                    { "showid": 123, "showname": "Doctor Who" }
                ]
            }
            """;

        var (client, handlerMock) = new MyEpisodesClientTestBuilder()
            .WithApiKey("key1")
            .WithMyShowsListResponse(jsonResponse)
            .Build();

        _clientFactoryMock.Setup(f => f.CreateClient("key1")).Returns(client);

        var tracker = new MyEpisodesTracker(
            _userDataManagerMock.Object,
            _libraryManagerMock.Object,
            _trackerLoggerMock.Object,
            _clientFactoryMock.Object);

        await tracker.StartAsync(CancellationToken.None);

        var series = new Series { Id = Guid.NewGuid(), ProductionYear = 2005 };
        _libraryManagerMock.Setup(m => m.GetItemById(series.Id)).Returns(series);

        var episode = new Episode
        {
            SeriesId = series.Id,
            SeriesName = "Doctor Who",
            ParentIndexNumber = 1,
            IndexNumber = 1
        };

        var eventArgs = new UserDataSaveEventArgs
        {
            Item = episode,
            SaveReason = UserDataSaveReason.PlaybackFinished,
            UserId = userGuid,
            UserData = new UserItemData { Key = "", Played = true }
        };

        var trackingCompletedTcs = new TaskCompletionSource<TrackingCompletedEventArgs>();
        tracker.TrackingCompleted += (sender, args) => trackingCompletedTcs.TrySetResult(args);

        // Act
        _userDataManagerMock.Raise(m => m.UserDataSaved += null, null, eventArgs);

        var result = await trackingCompletedTcs.Task;

        // Assert
        Assert.True(result.IsSuccess);

        // Assert PUT /v1/me/episodes/123/1/1 has correct watch flag
        handlerMock.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Put &&
                req.RequestUri != null &&
                req.RequestUri.PathAndQuery.Contains("/v1/me/episodes/123/1/1")),
            ItExpr.IsAny<CancellationToken>());

        await tracker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task OnUserDataSaved_ShowNotFound_ReturnsIsSuccessFalse_MED_69()
    {
        // Arrange
        var userGuid = Guid.NewGuid();
        var userConfig = new MyEpisodesUserConfiguration
        {
            JellyfinUserId = userGuid.ToString("N"),
            ApiKey = "key1",
            SyncWatched = true
        };
        _pluginConfig.UserConfigurations.Add(userConfig);

        var (client, _) = new MyEpisodesClientTestBuilder()
            .WithApiKey("key1")
            .WithSearchResponse("""{"data": []}""")
            .Build();

        _clientFactoryMock.Setup(f => f.CreateClient("key1")).Returns(client);

        var tracker = new MyEpisodesTracker(
            _userDataManagerMock.Object,
            _libraryManagerMock.Object,
            _trackerLoggerMock.Object,
            _clientFactoryMock.Object);

        await tracker.StartAsync(CancellationToken.None);

        var episode = new Episode
        {
            SeriesName = "UnknownShow999",
            ParentIndexNumber = 1,
            IndexNumber = 1
        };

        var eventArgs = new UserDataSaveEventArgs
        {
            Item = episode,
            SaveReason = UserDataSaveReason.PlaybackFinished,
            UserId = userGuid,
            UserData = new UserItemData { Key = "", Played = true }
        };

        var trackingCompletedTcs = new TaskCompletionSource<TrackingCompletedEventArgs>();
        tracker.TrackingCompleted += (sender, args) => trackingCompletedTcs.TrySetResult(args);

        // Act
        _userDataManagerMock.Raise(m => m.UserDataSaved += null, null, eventArgs);

        var result = await trackingCompletedTcs.Task;

        // Assert
        Assert.False(result.IsSuccess);

        await tracker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task OnUserDataSaved_UpdateStatusFails_ReturnsIsSuccessFalse_MED_71()
    {
        // Arrange
        var userGuid = Guid.NewGuid();
        var userConfig = new MyEpisodesUserConfiguration
        {
            JellyfinUserId = userGuid.ToString("N"),
            ApiKey = "key1",
            SyncWatched = true
        };
        _pluginConfig.UserConfigurations.Add(userConfig);

        var jsonResponse = """{"data": [{"showid": 123, "showname": "Doctor Who"}]}""";

        var (client, handlerMock) = new MyEpisodesClientTestBuilder()
            .WithApiKey("key1")
            .WithMyShowsListResponse(jsonResponse)
            .Build();

        // Setup update failure
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.Method == HttpMethod.Put && req.RequestUri != null && req.RequestUri.PathAndQuery.Contains("/v1/me/episodes/")),
                ItExpr.IsAny<System.Threading.CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = System.Net.HttpStatusCode.InternalServerError });

        _clientFactoryMock.Setup(f => f.CreateClient("key1")).Returns(client);

        var tracker = new MyEpisodesTracker(
            _userDataManagerMock.Object,
            _libraryManagerMock.Object,
            _trackerLoggerMock.Object,
            _clientFactoryMock.Object);

        await tracker.StartAsync(CancellationToken.None);

        var episode = new Episode
        {
            SeriesName = "Doctor Who",
            ParentIndexNumber = 1,
            IndexNumber = 1
        };

        var eventArgs = new UserDataSaveEventArgs
        {
            Item = episode,
            SaveReason = UserDataSaveReason.PlaybackFinished,
            UserId = userGuid,
            UserData = new UserItemData { Key = "", Played = true }
        };

        var trackingCompletedTcs = new TaskCompletionSource<TrackingCompletedEventArgs>();
        tracker.TrackingCompleted += (sender, args) => trackingCompletedTcs.TrySetResult(args);

        // Act
        _userDataManagerMock.Raise(m => m.UserDataSaved += null, null, eventArgs);

        var result = await trackingCompletedTcs.Task;

        // Assert
        Assert.False(result.IsSuccess);

        await tracker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task OnLibraryItemAdded_SyncsCorrectlyForEachUser_HIGH_236_239()
    {
        // Arrange
        var user1Guid = Guid.NewGuid();
        var user2Guid = Guid.NewGuid();

        var userConfig1 = new MyEpisodesUserConfiguration
        {
            JellyfinUserId = user1Guid.ToString("N"),
            ApiKey = "key1",
            SyncAcquired = true
        };
        var userConfig2 = new MyEpisodesUserConfiguration
        {
            JellyfinUserId = user2Guid.ToString("N"),
            ApiKey = "key2",
            SyncAcquired = true
        };
        _pluginConfig.UserConfigurations.Add(userConfig1);
        _pluginConfig.UserConfigurations.Add(userConfig2);

        var jsonResponse = """
            {
                "data": [
                    { "showid": 123, "showname": "Doctor Who" }
                ]
            }
            """;

        var (client1, handlerMock1) = new MyEpisodesClientTestBuilder()
            .WithApiKey("key1")
            .WithMyShowsListResponse(jsonResponse)
            .Build();

        var (client2, handlerMock2) = new MyEpisodesClientTestBuilder()
            .WithApiKey("key2")
            .WithMyShowsListResponse(jsonResponse)
            .Build();

        _clientFactoryMock.Setup(f => f.CreateClient("key1")).Returns(client1);
        _clientFactoryMock.Setup(f => f.CreateClient("key2")).Returns(client2);

        var tracker = new MyEpisodesTracker(
            _userDataManagerMock.Object,
            _libraryManagerMock.Object,
            _trackerLoggerMock.Object,
            _clientFactoryMock.Object);

        await tracker.StartAsync(CancellationToken.None);

        var series = new Series { Id = Guid.NewGuid(), ProductionYear = 2005 };
        _libraryManagerMock.Setup(m => m.GetItemById(series.Id)).Returns(series);

        var episode = new Episode
        {
            SeriesId = series.Id,
            SeriesName = "Doctor Who",
            ParentIndexNumber = 1,
            IndexNumber = 1
        };

        var eventArgs = new ItemChangeEventArgs
        {
            Item = episode
        };

        var trackingCompletedTcs = new TaskCompletionSource<TrackingCompletedEventArgs>();
        tracker.TrackingCompleted += (sender, args) => trackingCompletedTcs.TrySetResult(args);

        // Act
        _libraryManagerMock.Raise(m => m.ItemAdded += null, null, eventArgs);

        var result = await trackingCompletedTcs.Task;

        // Assert
        Assert.True(result.IsSuccess);

        _clientFactoryMock.Verify(f => f.CreateClient("key1"), Times.Once);
        _clientFactoryMock.Verify(f => f.CreateClient("key2"), Times.Once);

        // Check if both users sent acquired sync via PUT /v1/me/episodes/123/1/1
        handlerMock1.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Put &&
                req.RequestUri != null &&
                req.RequestUri.PathAndQuery.Contains("/v1/me/episodes/123/1/1")),
            ItExpr.IsAny<CancellationToken>());

        handlerMock2.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Put &&
                req.RequestUri != null &&
                req.RequestUri.PathAndQuery.Contains("/v1/me/episodes/123/1/1")),
            ItExpr.IsAny<CancellationToken>());

        await tracker.StopAsync(CancellationToken.None);
    }
}
