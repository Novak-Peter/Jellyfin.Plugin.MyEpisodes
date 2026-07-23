using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MyEpisodes;

public class MyEpisodesTracker : IHostedService, IDisposable
{
    private readonly IUserDataManager _userDataManager;
    private readonly ILogger<MyEpisodesTracker> _logger;
    private readonly IMyEpisodesClientFactory _clientFactory;
    private readonly Dictionary<string, MyEpisodesClient> _clients = new();
    private bool _isDisposed;

    public event EventHandler<TrackingCompletedEventArgs>? TrackingCompleted;

    public MyEpisodesTracker(IUserDataManager userDataManager, ILogger<MyEpisodesTracker> logger, IMyEpisodesClientFactory clientFactory)
    {
        _userDataManager = userDataManager;
        _logger = logger;
        _clientFactory = clientFactory;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("MyEpisodes: Starting playback and user data tracker");
        _userDataManager.UserDataSaved += OnUserDataSaved;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("MyEpisodes: Stopping playback and user data tracker");
        _userDataManager.UserDataSaved -= OnUserDataSaved;

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

        if (userConfig is not { SyncWatched: true } || string.IsNullOrEmpty(userConfig.Username) || string.IsNullOrEmpty(userConfig.Password))
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

        var played = e.UserData.Played;

        _logger.LogInformation("MyEpisodes: Queueing watched state sync for user {Username}. '{SeriesName}' S{Season}E{Episode} -> Played: {Played}",
            userConfig.Username, seriesName, seasonNumber.Value, episodeNumber.Value, played);

        _ = Task.Run(async () =>
        {
            try
            {
                var client = GetClientForUser(userConfig);
                var showId = await client.FindShowIdAsync(seriesName, productionYear).ConfigureAwait(false);

                if (showId == null)
                {
                    _logger.LogWarning("MyEpisodes: Could not resolve MyEpisodes show ID for series '{SeriesName}'", seriesName);
                    TrackingCompleted?.Invoke(this, new TrackingCompletedEventArgs { IsSuccess = false });
                    return;
                }

                var success = await client.SetEpisodeWatchedStateAsync(showId.Value, seasonNumber.Value, episodeNumber.Value, played).ConfigureAwait(false);
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

    private MyEpisodesClient GetClientForUser(MyEpisodesUserConfiguration userConfig)
    {
        var cacheKey = userConfig.JellyfinUserId;
        lock (_clients)
        {
            if (_clients.TryGetValue(cacheKey, out var existingClient))
            {
                if (existingClient.Username == userConfig.Username && existingClient.Password == userConfig.Password)
                {
                    return existingClient;
                }

                // Credentials changed, dispose old client
                _logger.LogInformation("MyEpisodes: Credentials changed for Jellyfin user {UserId}. Recreating client.", cacheKey);
                existingClient.Dispose();
                _clients.Remove(cacheKey);
            }

            var newClient = _clientFactory.CreateClient(userConfig.Username, userConfig.Password);
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


public class TrackingCompletedEventArgs : EventArgs {
    public bool IsSuccess { get; init; }
    public Exception? Exception { get; init; }
}