using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MyEpisodes
{
    public class MyEpisodesClient : IDisposable
    {
        private const string BaseUrl = "https://www.myepisodes.com";
        private readonly string _username;
        private readonly string _password;
        private readonly ILogger _logger;
        private readonly HttpClient _httpClient;
        public string Username => _username;
        public string Password => _password;
        private readonly CookieContainer _cookieContainer;
        private readonly Dictionary<string, int> _shows = new(StringComparer.OrdinalIgnoreCase);
        private static readonly AngleSharp.Html.Parser.HtmlParser _htmlParser = new AngleSharp.Html.Parser.HtmlParser();
        private bool _isLoggedIn;
        private bool _isDisposed;

        public MyEpisodesClient(string username, string password, ILogger logger)
        {
            _username = username;
            _password = password;
            _logger = logger;

            _cookieContainer = new CookieContainer();
            var handler = new HttpClientHandler
            {
                CookieContainer = _cookieContainer,
                UseCookies = true,
                AllowAutoRedirect = true
            };

            _httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(BaseUrl)
            };
            // Add user agent to mimic a standard browser
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
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

                var response = await _httpClient.PostAsync("/login/", content).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                // Verification matching Kodi plugin: check if the username (case-insensitive) is in the returned content
                if (html.Contains(_username, StringComparison.OrdinalIgnoreCase))
                {
                    _isLoggedIn = true;
                    _logger.LogInformation("MyEpisodes: Successfully logged in as {Username}", _username);
                    return true;
                }

                _logger.LogWarning("MyEpisodes: Login failed for {Username}. Username not found in page response.", _username);
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
                var response = await _httpClient.GetAsync("/myshows/list/").ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                // Parse HTML outside the lock (await not allowed inside lock)
                var document = await _htmlParser.ParseDocumentAsync(html).ConfigureAwait(false);
                var links = document.QuerySelectorAll("a[href^='/show/']");

                lock (_shows)
                {
                    _shows.Clear();
                    foreach (var link in links)
                    {
                        var href = link.GetAttribute("href");
                        // Expected format: /show/<id>/... or /show/<id>
                        var segments = href?.Split('/', StringSplitOptions.RemoveEmptyEntries);
                        if (segments != null && segments.Length >= 2 && int.TryParse(segments[1], out var id))
                        {
                            var name = link.TextContent.Trim();
                            var normalized = NormalizeShowName(name);
                            if (!string.IsNullOrEmpty(normalized) && !_shows.ContainsKey(normalized))
                            {
                                _shows[normalized] = id;
                            }
                        }
                    }
                }

                _logger.LogInformation("MyEpisodes: Populated {Count} shows from account", _shows.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MyEpisodes: Error populating shows for user {Username}", _username);
            }
        }

        public async Task<int?> FindShowIdAsync(string showName)
        {
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
                if (_shows.TryGetValue(normalizedName, out var cachedId))
                {
                    return cachedId;
                }

                // Try partial matches in local cache (startswith or contains)
                var matches = _shows.Where(x => normalizedName.Contains(x.Key) || x.Key.Contains(normalizedName)).ToList();
                if (matches.Count == 1)
                {
                    _logger.LogInformation("MyEpisodes: Found partial match in local cache for '{ShowName}': '{MatchedName}' (ID: {Id})", showName, matches[0].Key, matches[0].Value);
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

                var response = await _httpClient.PostAsync("/search/", content).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                // Find show links in search results
                var searchMatches = new List<(string Name, int Id)>();
                var document = await _htmlParser.ParseDocumentAsync(html).ConfigureAwait(false);
                var linkElements = document.QuerySelectorAll("a[href^='/show/']");
                foreach (var link in linkElements)
                {
                    var href = link.GetAttribute("href");
                    var segments = href?.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    if (segments != null && segments.Length >= 2 && int.TryParse(segments[1], out var id))
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

                // Match logic similar to Kodi:
                // a. Check exact match
                var exactMatch = searchMatches.FirstOrDefault(x => string.Equals(NormalizeShowName(x.Name), normalizedName, StringComparison.OrdinalIgnoreCase));
                if (exactMatch.Id != 0)
                {
                    _logger.LogInformation("MyEpisodes: Found exact match online for '{ShowName}' -> '{MatchedName}' (ID: {Id})", showName, exactMatch.Name, exactMatch.Id);
                    await AddShowAsync(exactMatch.Id).ConfigureAwait(false);
                    return exactMatch.Id;
                }

                // b. Check starts-with or partial match
                var partialMatches = searchMatches.Where(x => NormalizeShowName(x.Name).StartsWith(normalizedName) || normalizedName.StartsWith(NormalizeShowName(x.Name))).ToList();
                if (partialMatches.Count == 1)
                {
                    _logger.LogInformation("MyEpisodes: Found online partial match for '{ShowName}' -> '{MatchedName}' (ID: {Id})", showName, partialMatches[0].Name, partialMatches[0].Id);
                    await AddShowAsync(partialMatches[0].Id).ConfigureAwait(false);
                    return partialMatches[0].Id;
                }
                else if (partialMatches.Count > 1)
                {
                    // Pick the first one
                    _logger.LogInformation("MyEpisodes: Multiple partial matches online. Picking first: '{MatchedName}' (ID: {Id})", partialMatches[0].Name, partialMatches[0].Id);
                    await AddShowAsync(partialMatches[0].Id).ConfigureAwait(false);
                    return partialMatches[0].Id;
                }

                // c. Fallback: pick the first search match
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
            _logger.LogInformation("MyEpisodes: Adding show ID {ShowId} to {Username}'s account", showId, _username);
            try
            {
                var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "action", "add" },
                    { "showid", showId.ToString() }
                });

                var response = await _httpClient.PostAsync("/ajax/service.php?mode=show_manage", content).ConfigureAwait(false);
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

                var response = await _httpClient.PostAsync("/ajax/service.php?mode=eps_update", content).ConfigureAwait(false);
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
                if (disposing)
                {
                    _httpClient.Dispose();
                }
                _isDisposed = true;
            }
        }
    }
}
