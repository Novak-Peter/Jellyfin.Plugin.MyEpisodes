    using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MyEpisodes;

public class MyEpisodesClient : IDisposable
{
    private readonly string _username;
    private readonly string _password;
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    public string Username => _username;
    public string Password => _password;
    private readonly Dictionary<string, int> _shows = new(StringComparer.OrdinalIgnoreCase);
    private static readonly AngleSharp.Html.Parser.HtmlParser _htmlParser = new AngleSharp.Html.Parser.HtmlParser();
    private bool _isLoggedIn;
    private bool _isDisposed;

    public MyEpisodesClient(string username, string password, HttpClient httpClient, ILogger logger)
    {
        _username = username;
        _password = password;
        _httpClient = httpClient;
        _logger = logger;
    }

    public bool IsLoggedIn => _isLoggedIn;

    public async Task<bool> EnsureLoggedInAsync()
    {
        if (_isLoggedIn)
        {
            return true;
        }

        _logger.LogInformation("MyEpisodes: Attempting login for user {Username}", _username);
        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "username", _username },
                { "password", _password },
                { "action", "Login" },
                { "u", "" }
            });
            
            _logger.LogInformation("MyEpisodes: Attempting login for user {Username}", _username);

            var request = new HttpRequestMessage(HttpMethod.Post, "/login/")
            {
                Content = content
            };

            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            // Verify login by checking for the username (case-insensitive) is in the response HTML
            if (html.Contains(_username, StringComparison.OrdinalIgnoreCase))
            {
                _isLoggedIn = true;
                _logger.LogInformation("MyEpisodes: Successfully logged in as {Username}", _username);
                return true;
            }

            _logger.LogWarning("MyEpisodes: Login failed for {Username}. Username not found in response HTML.", _username);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MyEpisodes: Error logging in for user {Username}", _username);
            return false;
        }
    }

    public async Task PopulateShowsAsync()
    {
        if (!await EnsureLoggedInAsync().ConfigureAwait(false))
        {
            return;
        }

        _logger.LogInformation("MyEpisodes: Fetching shows list for {Username}", _username);
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/myshows/list/");
            
            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            // Parse HTML outside the lock (await not allowed inside lock)
            var document = await _htmlParser.ParseDocumentAsync(html).ConfigureAwait(false);
            var links = document.QuerySelectorAll("a[href^='/epsbyshow/']");

            lock (_shows)
            {
                _shows.Clear();
                foreach (var link in links)
                {
                    var href = link.GetAttribute("href");
                    var segments = href?.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    if (segments is { Length: >= 2 } && int.TryParse(segments[1], out var id))
                    {
                        var name = link.TextContent.Trim();
                        var normalized = NormalizeShowName(name);
                        if (!string.IsNullOrEmpty(normalized))
                        {
                            _shows.TryAdd(normalized, id);
                        }
                    }
                }
                _logger.LogInformation("MyEpisodes: Populated {Count} shows from account", _shows.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MyEpisodes: Error populating shows for user {Username}", _username);
        }
    }

    public async Task<int?> FindShowIdAsync(string showName, int? productionYear = null)
    {
        if (_shows.Count == 0)
        {
            await PopulateShowsAsync().ConfigureAwait(false);
        }

        if (string.IsNullOrEmpty(showName))
        {
            return null;
        }

        if (!await EnsureLoggedInAsync().ConfigureAwait(false))
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

        // 2. Search on MyEpisodes website
        _logger.LogInformation("MyEpisodes: '{ShowName}' not found in cache. Searching MyEpisodes.com...", showName);
        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "tvshow", showName },
                { "action", "Search" }
            });

            var request = new HttpRequestMessage(HttpMethod.Post, "/search/")
            {
                Content = content
            };

            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            // Find show links in search results
            var searchMatches = new List<(string Name, int Id)>();
            var document = await _htmlParser.ParseDocumentAsync(html).ConfigureAwait(false);
            var linkElements = document.QuerySelectorAll("a[href^='/epsbyshow/']");
            foreach (var link in linkElements)
            {
                var href = link.GetAttribute("href");
                var segments = href?.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments is { Length: >= 2 } && int.TryParse(segments[1], out var id))
                {
                    var name = link.TextContent.Trim();
                    searchMatches.Add((name, id));
                }
            }

            if (searchMatches.Count == 0)
            {
                _logger.LogWarning("MyEpisodes: Search returned no results for '{ShowName}'", showName);
                return null;
            }

            // a. Exact match on base name
            var exactMatches = searchMatches.Where(x => string.Equals(NormalizeShowName(x.Name), normalizedName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (exactMatches.Count == 1)
            {
                _logger.LogInformation("MyEpisodes: Found exact match online for '{ShowName}' -> '{MatchedName}' (ID: {Id})", showName, exactMatches[0].Name, exactMatches[0].Id);
                await AddShowAsync(exactMatches[0].Id).ConfigureAwait(false);
                return exactMatches[0].Id;
            }

            // b. Exact match on name (year)
            if (productionYear.HasValue)
            {
                var normalizedWithYear = NormalizeShowName($"{showName} ({productionYear.Value})");
                var exactMatchesWithYear = searchMatches.Where(x => string.Equals(NormalizeShowName(x.Name), normalizedWithYear, StringComparison.OrdinalIgnoreCase)).ToList();
                if (exactMatchesWithYear.Count == 1)
                {
                    _logger.LogInformation("MyEpisodes: Found exact match online with year for '{ShowName}' -> '{MatchedName}' (ID: {Id})", showName, exactMatchesWithYear[0].Name, exactMatchesWithYear[0].Id);
                    await AddShowAsync(exactMatchesWithYear[0].Id).ConfigureAwait(false);
                    return exactMatchesWithYear[0].Id;
                }
            }

            // c. Partial match on name + year
            if (productionYear.HasValue)
            {
                var yearStr = productionYear.Value.ToString();
                var partialMatchesWithYear = searchMatches.Where(x => {
                    var norm = NormalizeShowName(x.Name);
                    return (norm.Contains(normalizedName) || normalizedName.Contains(norm)) && norm.Contains(yearStr);
                }).ToList();

                if (partialMatchesWithYear.Count == 1)
                {
                    _logger.LogInformation("MyEpisodes: Found online partial match containing year '{Year}' for '{ShowName}' -> '{MatchedName}' (ID: {Id})", yearStr, showName, partialMatchesWithYear[0].Name, partialMatchesWithYear[0].Id);
                    await AddShowAsync(partialMatchesWithYear[0].Id).ConfigureAwait(false);
                    return partialMatchesWithYear[0].Id;
                }
                else if (partialMatchesWithYear.Count > 1)
                {
                    _logger.LogInformation("MyEpisodes: Multiple partial matches containing year '{Year}' online. Picking first: '{MatchedName}' (ID: {Id})", yearStr, partialMatchesWithYear[0].Name, partialMatchesWithYear[0].Id);
                    await AddShowAsync(partialMatchesWithYear[0].Id).ConfigureAwait(false);
                    return partialMatchesWithYear[0].Id;
                }
            }

            // d. Fallback to first exact match on base name if multiple existed
            if (exactMatches.Count > 1)
            {
                _logger.LogInformation("MyEpisodes: Multiple exact matches online. Picking first exact match: '{MatchedName}' (ID: {Id})", exactMatches[0].Name, exactMatches[0].Id);
                await AddShowAsync(exactMatches[0].Id).ConfigureAwait(false);
                return exactMatches[0].Id;
            }

            // e. Partial match on name only
            var partialMatches = searchMatches.Where(x => {
                var norm = NormalizeShowName(x.Name);
                return norm.Contains(normalizedName) || normalizedName.Contains(norm);
            }).ToList();

            if (partialMatches.Count == 1)
            {
                _logger.LogInformation("MyEpisodes: Found online partial match for '{ShowName}' -> '{MatchedName}' (ID: {Id})", showName, partialMatches[0].Name, partialMatches[0].Id);
                await AddShowAsync(partialMatches[0].Id).ConfigureAwait(false);
                return partialMatches[0].Id;
            }
            else if (partialMatches.Count > 1)
            {
                _logger.LogInformation("MyEpisodes: Multiple partial matches online. Picking first: '{MatchedName}' (ID: {Id})", partialMatches[0].Name, partialMatches[0].Id);
                await AddShowAsync(partialMatches[0].Id).ConfigureAwait(false);
                return partialMatches[0].Id;
            }

            // Fallback: pick the first search match
            _logger.LogInformation("MyEpisodes: No precise match online. Picking first result: '{MatchedName}' (ID: {Id})", searchMatches[0].Name, searchMatches[0].Id);
            await AddShowAsync(searchMatches[0].Id).ConfigureAwait(false);
            return searchMatches[0].Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MyEpisodes: Error searching online for '{ShowName}'", showName);
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

        _logger.LogInformation("MyEpisodes: Adding show ID {ShowId} to {Username}'s account", showId, _username);
        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "action", "add" },
                { "showid", showId.ToString() }
            });

            var request = new HttpRequestMessage(HttpMethod.Post, "/ajax/service.php?mode=show_manage")
            {
                Content = content
            };

            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            // Refresh show cache list
            await PopulateShowsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MyEpisodes: Error adding show ID {ShowId}", showId);
        }
    }

    public async Task<bool> SetEpisodeWatchedStateAsync(int showId, int seasonNumber, int episodeNumber, bool watched)
    {
        if (!await EnsureLoggedInAsync().ConfigureAwait(false))
        {
            return false;
        }

        _logger.LogInformation("MyEpisodes: Setting watched state for Show ID {ShowId}, S{Season}E{Episode} to {Watched} for {Username}",
            showId, seasonNumber, episodeNumber, watched, _username);

        try
        {
            // Key format: V[show_id]-[season]-[episode]
            var key = $"V{showId}-{seasonNumber}-{episodeNumber}";
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { key, watched.ToString().ToLowerInvariant() }
            });

            var request = new HttpRequestMessage(HttpMethod.Post, "/ajax/service.php?mode=eps_update")
            {
                Content = content
            };
            // Mimic browser request headers
            request.Headers.Accept.Clear();
            request.Headers.Accept.ParseAdd("application/json, text/javascript, */*; q=0.01");
            request.Headers.Referrer = new Uri($"https://www.myepisodes.com/show/id-{showId}/");
            request.Headers.AcceptLanguage.Clear();
            request.Headers.AcceptLanguage.ParseAdd("en-US,en-GB;q=0.9,en;q=0.8,hu-HU;q=0.7,hu;q=0.6");
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            request.Headers.Add("Origin", "https://www.myepisodes.com");
            request.Headers.Add("Cache-Control", "no-cache");
            request.Headers.Pragma.ParseAdd("no-cache");
            request.Headers.Add("Priority", "u=1, i");

            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("MyEpisodes: Successfully updated watched state for Show ID {ShowId}, S{Season}E{Episode} to {Watched}",
                showId, seasonNumber, episodeNumber, watched);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MyEpisodes: Error updating watched state for Show ID {ShowId}, S{Season}E{Episode}", showId, seasonNumber, episodeNumber);
            return false;
        }
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
            _isDisposed = true;
        }
    }
}