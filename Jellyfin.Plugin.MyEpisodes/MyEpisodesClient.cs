using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MyEpisodes;

public enum EpisodeStatus
{
    Acquired,
    Unacquired,
    Watched,
    Unwatched,
}

public class ShowDto
{
    [JsonPropertyName("showid")]
    public int ShowId { get; set; }

    [JsonPropertyName("showname")]
    public string ShowName { get; set; } = string.Empty;
}

public class ShowsResponseDto
{
    [JsonPropertyName("data")]
    public List<ShowDto>? Data { get; set; }
}

public class EpisodeUpdateItemDto
{
    [JsonPropertyName("showid")]
    public int ShowId { get; set; }

    [JsonPropertyName("season")]
    public int Season { get; set; }

    [JsonPropertyName("episode")]
    public int Episode { get; set; }

    [JsonPropertyName("watched")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Watched { get; set; }

    [JsonPropertyName("acquired")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Acquired { get; set; }
}

public class BulkEpisodeRequestDto
{
    [JsonPropertyName("episodes")]
    public List<EpisodeUpdateItemDto> Episodes { get; set; } = new();
}

public class MyEpisodesClient : IDisposable
{
    private readonly string _apiKey;
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly Dictionary<string, int> _shows = new(StringComparer.OrdinalIgnoreCase);
    private bool _isDisposed;

    public MyEpisodesClient(string apiKey, HttpClient httpClient, ILogger logger)
    {
        _apiKey = apiKey;
        _httpClient = httpClient;
        _logger = logger;
    }

    public string ApiKey => _apiKey;

    public async Task PopulateShowsAsync()
    {
        _logger.LogInformation("MyEpisodes: Fetching followed shows list via API");
        try
        {
            var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, "/v1/me/shows?limit=1000")).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<ShowsResponseDto>().ConfigureAwait(false);
            if (result?.Data is not null)
            {
                lock (_shows)
                {
                    _shows.Clear();
                    foreach (var show in result.Data)
                    {
                        var normalized = NormalizeShowName(show.ShowName);
                        if (!string.IsNullOrEmpty(normalized))
                        {
                            _shows.TryAdd(normalized, show.ShowId);
                        }
                    }
                    _logger.LogInformation("MyEpisodes: Populated {Count} shows from account API", _shows.Count);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MyEpisodes: Error populating shows list via API");
        }
    }

    public async Task<int?> FindOrAddShowAsync(string showName, int? productionYear = null)
    {
        if (_shows.Count == 0)
        {
            await PopulateShowsAsync().ConfigureAwait(false);
        }

        if (string.IsNullOrEmpty(showName))
        {
            return null;
        }
        
        var normalizedName = NormalizeShowName(showName);
        if (string.IsNullOrEmpty(normalizedName))
        {
            return null;
        }

        // 1. Try local cache
        lock (_shows)
        {
            // a. Exact match on base name
            if (_shows.TryGetValue(normalizedName, out var cachedId))
            {
                return cachedId;
            }

            // b. Exact match on name (year)
            if (productionYear.HasValue)
            {
                var normalizedWithYear = NormalizeShowName($"{showName} ({productionYear.Value})");
                if (_shows.TryGetValue(normalizedWithYear, out var cachedIdWithYear))
                {
                    return cachedIdWithYear;
                }
            }

            // c. Partial match on name + year
            if (productionYear.HasValue)
            {
                var yearStr = productionYear.Value.ToString();
                var yearMatches = _shows
                    .Where(x => (x.Key.Contains(normalizedName) || normalizedName.Contains(x.Key)) && x.Key.Contains(yearStr))
                    .ToList();
                if (yearMatches.Count == 1)
                {
                    _logger.LogInformation("MyEpisodes: Found partial match with year in local cache for '{ShowName}': '{MatchedName}' (ID: {Id})", showName, yearMatches[0].Key, yearMatches[0].Value);
                    return yearMatches[0].Value;
                }
                else if (yearMatches.Count > 1)
                {
                    _logger.LogInformation("MyEpisodes: Multiple partial matches with year in local cache. Picking first: '{MatchedName}' (ID: {Id})", yearMatches[0].Key, yearMatches[0].Value);
                    return yearMatches[0].Value;
                }
            }

            // d. Partial match on name only
            var matches = _shows.Where(x => normalizedName.Contains(x.Key) || x.Key.Contains(normalizedName)).ToList();
            if (matches.Count == 1)
            {
                _logger.LogInformation("MyEpisodes: Found partial match in local cache for '{ShowName}': '{MatchedName}' (ID: {Id})", showName, matches[0].Key, matches[0].Value);
                return matches[0].Value;
            }
            else if (matches.Count > 1)
            {
                _logger.LogInformation("MyEpisodes: Multiple partial matches in local cache. Picking first: '{MatchedName}' (ID: {Id})", matches[0].Key, matches[0].Value);
                return matches[0].Value;
            }
        }

        // 2. Search on MyEpisodes API catalogue
        _logger.LogInformation("MyEpisodes: '{ShowName}' not found in cache. Searching MyEpisodes API catalogue...", showName);
        try
        {
            var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, $"/v1/shows?search={Uri.EscapeDataString(showName)}")).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<ShowsResponseDto>().ConfigureAwait(false);
            var searchMatches = result?.Data;

            if (searchMatches == null || searchMatches.Count == 0)
            {
                _logger.LogWarning("MyEpisodes: API Search returned no results for '{ShowName}'", showName);
                return null;
            }

            // a. Exact match on base name
            var exactMatches = searchMatches.Where(x => string.Equals(NormalizeShowName(x.ShowName), normalizedName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (exactMatches.Count == 1)
            {
                _logger.LogInformation("MyEpisodes: Found exact match via API for '{ShowName}' -> '{MatchedName}' (ID: {Id})", showName, exactMatches[0].ShowName, exactMatches[0].ShowId);
                await AddShowAsync(exactMatches[0].ShowId).ConfigureAwait(false);
                return exactMatches[0].ShowId;
            }

            // b. Exact match on name (year)
            if (productionYear.HasValue)
            {
                var normalizedWithYear = NormalizeShowName($"{showName} ({productionYear.Value})");
                var exactMatchesWithYear = searchMatches.Where(x => string.Equals(NormalizeShowName(x.ShowName), normalizedWithYear, StringComparison.OrdinalIgnoreCase)).ToList();
                if (exactMatchesWithYear.Count == 1)
                {
                    _logger.LogInformation("MyEpisodes: Found exact match online with year for '{ShowName}' -> '{MatchedName}' (ID: {Id})", showName, exactMatchesWithYear[0].ShowName, exactMatchesWithYear[0].ShowId);
                    await AddShowAsync(exactMatchesWithYear[0].ShowId).ConfigureAwait(false);
                    return exactMatchesWithYear[0].ShowId;
                }
            }

            // c. Partial match on name + year
            if (productionYear.HasValue)
            {
                var yearStr = productionYear.Value.ToString();
                var partialMatchesWithYear = searchMatches.Where(x => {
                    var norm = NormalizeShowName(x.ShowName);
                    return (norm.Contains(normalizedName) || normalizedName.Contains(norm)) && norm.Contains(yearStr);
                }).ToList();

                if (partialMatchesWithYear.Count == 1)
                {
                    _logger.LogInformation("MyEpisodes: Found online partial match containing year '{Year}' for '{ShowName}' -> '{MatchedName}' (ID: {Id})", yearStr, showName, partialMatchesWithYear[0].ShowName, partialMatchesWithYear[0].ShowId);
                    await AddShowAsync(partialMatchesWithYear[0].ShowId).ConfigureAwait(false);
                    return partialMatchesWithYear[0].ShowId;
                }
                else if (partialMatchesWithYear.Count > 1)
                {
                    _logger.LogInformation("MyEpisodes: Multiple partial matches containing year '{Year}' online. Picking first: '{MatchedName}' (ID: {Id})", yearStr, partialMatchesWithYear[0].ShowName, partialMatchesWithYear[0].ShowId);
                    await AddShowAsync(partialMatchesWithYear[0].ShowId).ConfigureAwait(false);
                    return partialMatchesWithYear[0].ShowId;
                }
            }

            // d. Fallback to first exact match if multiple existed
            if (exactMatches.Count > 1)
            {
                _logger.LogInformation("MyEpisodes: Multiple exact matches online. Picking first: '{MatchedName}' (ID: {Id})", exactMatches[0].ShowName, exactMatches[0].ShowId);
                await AddShowAsync(exactMatches[0].ShowId).ConfigureAwait(false);
                return exactMatches[0].ShowId;
            }

            // e. Partial match on name only
            var partialMatches = searchMatches.Where(x => {
                var norm = NormalizeShowName(x.ShowName);
                return norm.Contains(normalizedName) || normalizedName.Contains(norm);
            }).ToList();

            if (partialMatches.Count == 1)
            {
                _logger.LogInformation("MyEpisodes: Found online partial match for '{ShowName}' -> '{MatchedName}' (ID: {Id})", showName, partialMatches[0].ShowName, partialMatches[0].ShowId);
                await AddShowAsync(partialMatches[0].ShowId).ConfigureAwait(false);
                return partialMatches[0].ShowId;
            }
            else if (partialMatches.Count > 1)
            {
                _logger.LogInformation("MyEpisodes: Multiple partial matches online. Picking first: '{MatchedName}' (ID: {Id})", partialMatches[0].ShowName, partialMatches[0].ShowId);
                await AddShowAsync(partialMatches[0].ShowId).ConfigureAwait(false);
                return partialMatches[0].ShowId;
            }

            // Fallback: pick the first search match
            _logger.LogInformation("MyEpisodes: No precise match online. Picking first result: '{MatchedName}' (ID: {Id})", searchMatches[0].ShowName, searchMatches[0].ShowId);
            await AddShowAsync(searchMatches[0].ShowId).ConfigureAwait(false);
            return searchMatches[0].ShowId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MyEpisodes: Error searching online API for '{ShowName}'", showName);
            return null;
        }
    }

    public async Task AddShowAsync(int showId)
    {
        lock (_shows)
        {
            if (_shows.ContainsValue(showId))
            {
                return;
            }
        }

        _logger.LogInformation("MyEpisodes: Adding show ID {ShowId} to account via API", showId);
        try
        {
            var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Put, $"/v1/me/shows/{showId}")).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            // Refresh show cache list
            await PopulateShowsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MyEpisodes: Error adding show ID {ShowId} via API", showId);
        }
    }

    public async Task<bool> UpdateEpisodeStatus(int showId, int seasonNumber, int episodeNumber, EpisodeStatus episodeStatus)
    {
        _logger.LogInformation("MyEpisodes: Setting status for Show ID {ShowId}, S{Season}E{Episode} to {Status}",
            showId, seasonNumber, episodeNumber, episodeStatus);

        var payload = episodeStatus switch
        {
            EpisodeStatus.Watched => (object)new { watched = true },
            EpisodeStatus.Unwatched => new { watched = false },
            EpisodeStatus.Acquired => new { acquired = true },
            EpisodeStatus.Unacquired => new { acquired = false },
            _ => throw new ArgumentOutOfRangeException(nameof(episodeStatus), episodeStatus, null)
        };

        try
        {
            var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Put, $"/v1/me/episodes/{showId}/{seasonNumber}/{episodeNumber}")
            {
                Content = JsonContent.Create(payload)
            }).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
            _logger.LogInformation("MyEpisodes: Successfully updated Show ID {ShowId}, S{Season}E{Episode} to {Status}",
                showId, seasonNumber, episodeNumber, episodeStatus);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MyEpisodes: Error updating episode status for Show ID {ShowId}, S{Season}E{Episode}", showId, seasonNumber, episodeNumber);
            return false;
        }
    }

    public async Task<bool> UpdateEpisodesBulkAsync(List<EpisodeUpdateItemDto> episodes)
    {
        if (episodes == null || episodes.Count == 0)
        {
            return true;
        }

        _logger.LogInformation("MyEpisodes: Bulk updating {Count} episodes via API", episodes.Count);

        try
        {
            // Chunk episodes into batches of 500 max as allowed by MyEpisodes API
            const int chunkSize = 500;
            var allSuccess = true;

            for (var i = 0; i < episodes.Count; i += chunkSize)
            {
                var chunk = episodes.Skip(i).Take(chunkSize).ToList();
                var payload = new BulkEpisodeRequestDto { Episodes = chunk };

                var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Post, "/v1/me/episodes")
                {
                    Content = JsonContent.Create(payload)
                }).ConfigureAwait(false);

                response.EnsureSuccessStatusCode();
                _logger.LogInformation("MyEpisodes: Successfully sent bulk update chunk of {Count} episodes", chunk.Count);
            }

            return allSuccess;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MyEpisodes: Error sending bulk episode update via API");
            return false;
        }
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> requestFactory, int maxRetries = 3)
    {
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            var request = requestFactory();
            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < maxRetries)
            {
                var retryAfter = response.Headers.RetryAfter;
                var delay = retryAfter?.Delta ?? TimeSpan.FromSeconds(1);
                _logger.LogWarning("MyEpisodes API Rate limited (429). Retrying after {Seconds} seconds (Attempt {Attempt}/{Max})",
                    delay.TotalSeconds, attempt + 1, maxRetries);
                await Task.Delay(delay).ConfigureAwait(false);
                continue;
            }

            return response;
        }

        return await _httpClient.SendAsync(requestFactory()).ConfigureAwait(false);
    }

    private string NormalizeShowName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        var sanitized = name.ToLowerInvariant();
        // Replace punctuation and characters like in the Kodi plugin sanitization
        foreach (var c in new[] { '[', ']', '_', '(', ')', '.', '-' })
        {
            sanitized = sanitized.Replace(c, ' ');
        }
        // Normalize spaces
        sanitized = Regex.Replace(sanitized, @"\s+", " ").Trim();
        return sanitized;
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
                _httpClient.Dispose();
            }
            _isDisposed = true;
        }
    }
}