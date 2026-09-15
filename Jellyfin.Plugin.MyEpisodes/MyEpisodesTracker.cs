using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MyEpisodes;

public class MyEpisodesTracker : IHostedService, IDisposable
{
    private readonly IUserDataManager _userDataManager;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<MyEpisodesTracker> _logger;
    private readonly IMyEpisodesClientFactory _clientFactory;
    private readonly Dictionary<string, MyEpisodesClient> _clients = new();
    private bool _isDisposed;

    public event EventHandler<TrackingCompletedEventArgs>? TrackingCompleted;

    public MyEpisodesTracker(IUserDataManager userDataManager, ILibraryManager libraryManager, ILogger<MyEpisodesTracker> logger, IMyEpisodesClientFactory clientFactory)
    {
        _userDataManager = userDataManager;
        _libraryManager = libraryManager;
        _logger = logger;
        _clientFactory = clientFactory;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("MyEpisodes: Starting playback and user data tracker");
        _userDataManager.UserDataSaved += OnUserDataSaved;
        _libraryManager.ItemAdded += OnLibraryItemAdded;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("MyEpisodes: Stopping playback and user data tracker");
        _userDataManager.UserDataSaved -= OnUserDataSaved;
        _libraryManager.ItemAdded -= OnLibraryItemAdded;

        lock (_clients)
        {
            foreach (var client in _clients.Values)
            {
                client.Dispose();
            }
            _clients.Clear();
        }

        return Task.CompletedTask;
    }

    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
    {
        if (e.Item is not Episode episode)
        {
            TrackingCompleted?.Invoke(this, new TrackingCompletedEventArgs { IsSuccess = false });
            return;
        }

        // Only act when user explicitly toggles played status or playback finishes
        if (e.SaveReason != UserDataSaveReason.TogglePlayed && e.SaveReason != UserDataSaveReason.PlaybackFinished)
        {
            TrackingCompleted?.Invoke(this, new TrackingCompletedEventArgs { IsSuccess = false });
            return;
        }

        var config = Plugin.Instance?.Configuration;
        if (config == null)
        {
            TrackingCompleted?.Invoke(this, new TrackingCompletedEventArgs { IsSuccess = false });
            return;
        }

        var userIdStr = e.UserId.ToString("N");
        var userConfig = config.UserConfigurations.FirstOrDefault(u =>
            string.Equals(u.JellyfinUserId, userIdStr, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(u.JellyfinUserId, e.UserId.ToString(), StringComparison.OrdinalIgnoreCase));

        if (userConfig is not { SyncWatched: true } || string.IsNullOrEmpty(userConfig.ApiKey))
        {
            TrackingCompleted?.Invoke(this, new TrackingCompletedEventArgs { IsSuccess = false });
            return;
        }

        var seriesName = episode.SeriesName;
        var seasonNumber = episode.ParentIndexNumber;
        var episodeNumber = episode.IndexNumber;
        var productionYear = episode.Series?.ProductionYear;

        if (string.IsNullOrEmpty(seriesName) || seasonNumber == null || episodeNumber == null)
        {
            _logger.LogWarning("MyEpisodes: Missing metadata for episode. Series: '{SeriesName}', Season: {Season}, Episode: {Episode}",
                seriesName ?? "Unknown", seasonNumber, episodeNumber);
            TrackingCompleted?.Invoke(this, new TrackingCompletedEventArgs { IsSuccess = false });
            return;
        }

        var status = e.UserData.Played ? EpisodeStatus.Watched : EpisodeStatus.Unwatched;

        _logger.LogInformation("MyEpisodes: Queueing watched state sync for user {UserId}. '{SeriesName}' S{Season}E{Episode} -> Status: {Status}",
            userConfig.JellyfinUserId, seriesName, seasonNumber.Value, episodeNumber.Value, status);

        _ = Task.Run(async () =>
        {
            try
            {
                var client = GetClientForUser(userConfig);
                var showId = await client.FindOrAddShowAsync(seriesName, productionYear).ConfigureAwait(false);

                if (showId == null)
                {
                    _logger.LogWarning("MyEpisodes: Could not resolve MyEpisodes show ID for series '{SeriesName}'", seriesName);
                    TrackingCompleted?.Invoke(this, new TrackingCompletedEventArgs { IsSuccess = false });
                    return;
                }

                var success = await client.UpdateEpisodeStatus(showId.Value, seasonNumber.Value, episodeNumber.Value, status).ConfigureAwait(false);
                if (success)
                {
                    _logger.LogInformation("MyEpisodes: Successfully synced S{Season}E{Episode} of '{SeriesName}' to MyEpisodes.com",
                        seasonNumber.Value, episodeNumber.Value, seriesName);
                    TrackingCompleted?.Invoke(this, new TrackingCompletedEventArgs { IsSuccess = true });
                }
                else
                {
                    _logger.LogWarning("MyEpisodes: Failed to sync S{Season}E{Episode} of '{SeriesName}' to MyEpisodes.com",
                        seasonNumber.Value, episodeNumber.Value, seriesName);
                    TrackingCompleted?.Invoke(this, new TrackingCompletedEventArgs { IsSuccess = false });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MyEpisodes: Exception error while syncing episode S{Season}E{Episode} of '{SeriesName}'",
                    seasonNumber.Value, episodeNumber.Value, seriesName);
                TrackingCompleted?.Invoke(this, new TrackingCompletedEventArgs { IsSuccess = false, Exception = ex });
            }
        });
    }

    private void OnLibraryItemAdded(object? sender, ItemChangeEventArgs e)
    {
        if (e.Item is not Episode episode)
        {
            TrackingCompleted?.Invoke(this, new TrackingCompletedEventArgs { IsSuccess = false });
            return;
        }

        var seriesName = episode.SeriesName;
        var seasonNumber = episode.ParentIndexNumber;
        var episodeNumber = episode.IndexNumber;
        var productionYear = episode.Series?.ProductionYear;

        if (string.IsNullOrEmpty(seriesName) || seasonNumber == null || episodeNumber == null)
        {
            _logger.LogWarning("MyEpisodes: Missing metadata for episode. Series: '{SeriesName}', Season: {Season}, Episode: {Episode}",
                seriesName ?? "Unknown", seasonNumber, episodeNumber);
            TrackingCompleted?.Invoke(this, new TrackingCompletedEventArgs { IsSuccess = false });
            return;
        }

        var config = Plugin.Instance?.Configuration;
        if (config == null)
        {
            TrackingCompleted?.Invoke(this, new TrackingCompletedEventArgs { IsSuccess = false });
            return;
        }

        var userConfigs = config.UserConfigurations.Where(userConfig =>
            userConfig is { SyncAcquired: true }
            && !string.IsNullOrWhiteSpace(userConfig.JellyfinUserId)
            && !string.IsNullOrEmpty(userConfig.ApiKey)).ToList();

        if (userConfigs.Count == 0)
        {
            _logger.LogInformation("MyEpisodes: No user configuration is found to track acquiring Series: '{SeriesName}', Season: {Season}, Episode: {Episode}",
                seriesName, seasonNumber, episodeNumber);
            TrackingCompleted?.Invoke(this, new TrackingCompletedEventArgs { IsSuccess = false });
            return;
        }

        _ = Task.Run(async () =>
        {
            var aggregatedExceptions = new List<Exception>(userConfigs.Count);
            var aggregatedSuccess = true;
            foreach (var userConfig in userConfigs)
            {
                try
                {
                    var client = GetClientForUser(userConfig);
                    var showId = await client.FindOrAddShowAsync(seriesName, productionYear).ConfigureAwait(false);

                    if (showId == null)
                    {
                        _logger.LogWarning("MyEpisodes: Could not resolve MyEpisodes show ID for series '{SeriesName}'",
                            seriesName);
                        aggregatedSuccess = false;
                        continue;
                    }
                    var success = await client
                        .UpdateEpisodeStatus(showId.Value, seasonNumber.Value, episodeNumber.Value,
                            EpisodeStatus.Acquired).ConfigureAwait(false);
                    if (success)
                    {
                        _logger.LogInformation(
                            "MyEpisodes: Successfully synced Acquired status S{Season}E{Episode} of '{SeriesName}' to MyEpisodes.com for User {UserId}",
                            seasonNumber.Value, episodeNumber.Value, seriesName, userConfig.JellyfinUserId);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "MyEpisodes: Failed to sync Acquired status S{Season}E{Episode} of '{SeriesName}' to MyEpisodes.com for User {UserId}",
                            seasonNumber.Value, episodeNumber.Value, seriesName, userConfig.JellyfinUserId);
                    }
                    aggregatedSuccess &= success;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "MyEpisodes: Exception error while syncing acquired status for episode S{Season}E{Episode} of '{SeriesName}' for User {UserId}",
                        seasonNumber.Value, episodeNumber.Value, seriesName, userConfig.JellyfinUserId);
                    aggregatedExceptions.Add(ex);
                    aggregatedSuccess = false;
                }
            }
            TrackingCompleted?.Invoke(this, new TrackingCompletedEventArgs
            {
                IsSuccess = aggregatedSuccess,
                Exception = aggregatedExceptions.Count > 1 ? new AggregateException(aggregatedExceptions) : aggregatedExceptions.FirstOrDefault()
            });
        });
    }

    private MyEpisodesClient GetClientForUser(MyEpisodesUserConfiguration userConfig)
    {
        var cacheKey = userConfig.JellyfinUserId;
        lock (_clients)
        {
            if (_clients.TryGetValue(cacheKey, out var existingClient))
            {
                if (existingClient.ApiKey == userConfig.ApiKey)
                {
                    return existingClient;
                }

                _logger.LogInformation("MyEpisodes: ApiKey changed for Jellyfin user {UserId}. Recreating client.", cacheKey);
                existingClient.Dispose();
                _clients.Remove(cacheKey);
            }

            var newClient = _clientFactory.CreateClient(userConfig.ApiKey);
            _clients[cacheKey] = newClient;
            return newClient;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            if (disposing)
            {
                _userDataManager.UserDataSaved -= OnUserDataSaved;
                lock (_clients)
                {
                    foreach (var client in _clients.Values)
                    {
                        client.Dispose();
                    }
                    _clients.Clear();
                }
            }
            _isDisposed = true;
        }
    }
}

public class TrackingCompletedEventArgs : EventArgs
{
    public bool IsSuccess { get; init; }
    public Exception? Exception { get; init; }
}