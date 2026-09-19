# MyEpisodes Plugin — Unit Test Specification

Test spec for `Jellyfin.Plugin.MyEpisodes`. Implement as xUnit tests using the existing
`MockBehavior.Strict` + `Moq.Protected` `HttpMessageHandler` seam. Assert on **outgoing HTTP**
(method + path + form body + headers) wherever possible rather than internal state.

Priorities: **[HIGH]** = guards against the data-loss / leak / silent-failure bugs already found;
implement first. **[MED]** = correctness of flows hit on every sync. **[LOW]** = edges & hygiene.

  ---

## 0. Required test-fixture changes (do these first)

The current `MyEpisodesClientTestBuilder` blocks several tests. Fix before writing cases:

1. **Login HTML fixture is unrealistic.** `_loginHtml = "Welcome testuser!"` has the username but no
   `/logout/` marker. Replace with realistic logged-in markup containing the logout anchor, e.g.:
   ```html
   <a href="/logout/"><strong>testuser</strong> (Logout)</a>
   Also add a logged-out fixture variant (login form / "Change Login", no /logout/).
2. Add WithMyShowsListResponse(string html) — currently /myshows/list/ always returns
   <html></html>, so the cache is never populated and the entire cache-hit path is untestable.
3. Add WithEpsUpdateResponse(string body, HttpStatusCode status = OK) — eps_update has no stub
   today; SetEpisodeWatchedStateAsync is completely untested.
4. Stop discarding the handler mock — most tests use var (client, _). Keep it so call-count /
   never-called verifications work.
5. Guard req.RequestUri?.PathAndQuery against null in matchers to avoid cryptic strict-mock failures.
6. Provide a way to build a client over a wrapped/disposable-tracking handler for the Dispose tests.

  ---
1. MyEpisodesClient — Dispose  (leak regression)

- [HIGH] 56 Dispose() disposes the underlying HttpClient (verify via tracking handler / disposed probe).
- [HIGH] 57 Double Dispose() is safe and idempotent (no throw).
- [LOW] 58 Using the client after dispose behaves predictably (ObjectDisposedException or guarded).

2. MyEpisodesClient — FindShowIdAsync  (the data-wipe regression area)

- [HIGH] 16 Show already in cache → returns cached id and AddShowAsync / show_manage is NOT called.
- [HIGH] 17 Show resolved from cache → no /search/ request issued.
- [HIGH] 14 _shows empty on entry → PopulateShowsAsync (/myshows/list/) invoked before cache lookup.
- [HIGH] 15 _shows already populated → PopulateShowsAsync not called again (Count==0 guard).
- [MED] 18 Exact match on normalized base name (from cache) → returns id.
- [MED] 19 Exact match on "name (year)" when productionYear given.
- [MED] 20 Partial match + year, single hit → returns it.
- [MED] 21 Partial match + year, multiple hits → returns first.
- [MED] 22 Partial match name-only, single hit → returns it.
- [MED] 23 Partial match name-only, multiple hits → returns first.
- [MED] 24 Not in cache → POSTs /search/ with tvshow + action=Search.
- [MED] 25 Search returns zero results → returns null.
- [MED] 26 Search exact match (single) → calls AddShowAsync, returns id.
- [MED] 27 Search exact match with year (single) → adds + returns.
- [MED] 28 Search partial+year single/multiple → adds + returns (first on multiple).
- [MED] 29 Multiple exact matches → picks first exact, adds + returns.
- [MED] 30 Partial name-only single/multiple → adds + returns (first on multiple).
- [MED] 31 No precise match → falls back to first search result, adds + returns.
- [MED] 33 Null/empty showName → returns null immediately, no network.
- [MED] 34 Not logged in / login fails → returns null.
- [LOW] 35 showName normalizing to empty → returns null.
- [LOW] 32 Search throws → caught, returns null.
- [LOW] 36 Very short normalized name that substring-matches many cached shows → assert deterministic
  (currently first). Documents the over-eager-match risk.

▎ Note: existing year-disambiguation tests (Doctor Who 2005 vs 1963) cover 19–23 via the search path.
▎ Add cache-backed equivalents using WithMyShowsListResponse, since the cache path is where the bug lived.

3. MyEpisodesClient — AddShowAsync  (idempotency guard)

- [HIGH] 37 showId already in _shows (by value) → returns early, no show_manage POST, no populate.
- [HIGH] 38 showId not tracked → POSTs action=add + showid, then calls PopulateShowsAsync to refresh.
- [HIGH] 41 Empty _shows → guard's ContainsValue is false → add proceeds (documents guard depends on warm cache).
- [LOW] 39 Guard read happens under lock (_shows) (assert via concurrency test or design review).
- [LOW] 40 HTTP error on add → caught, logged, no throw.

4. MyEpisodesClient — SetEpisodeWatchedStateAsync  (the actual "mark watched" op — untested today)

- [HIGH] 42 watched=true → POSTs key V{showId}-{season}-{episode}=true to eps_update.
- [HIGH] 43 watched=false → POSTs ...=false (lowercase). Verifies the unwatch path really sends false.
- [HIGH] 47 Success (2xx) → returns true.
- [HIGH] 45 Not logged in / login fails → returns false, no POST.
- [MED] 44 Correct headers: Accept, Referer=/show/id-{showId}/, X-Requested-With, Origin.
- [MED] 46 Non-2xx → EnsureSuccessStatusCode throws → caught → returns false.
- [MED] 48 Exception during send → caught, returns false.
- [LOW] 49 200 with empty/logged-out JSON → logs a warning (visibility only; no re-login — per design decision).

5. MyEpisodesClient — EnsureLoggedInAsync

- [MED] 1 First call, valid creds → POSTs /login/ with username,password,action=Login,u="";
  response has /logout/ marker + username → returns true, sets IsLoggedIn.
- [MED] 2 Already logged in → returns true with no second /login/ POST.
- [MED] 3 Login fails (no logout marker) → returns false, IsLoggedIn stays false.
- [MED] 6 HTML has username but no /logout/ link → treated as not-logged-in (stricter check).
- [LOW] 4 Login throws → caught, returns false, no propagation.
- [LOW] 5 Non-success status on /login/ → caught → returns false.

6. MyEpisodesClient — PopulateShowsAsync

- [HIGH] 9 _shows.Clear() before refill → calling twice does not accumulate stale entries.
- [MED] 8 Successful fetch → parses a[href^='/epsbyshow/'], populates normalized name → id.
- [MED] 10 Malformed hrefs (missing/non-numeric id) skipped without throwing.
- [MED] 11 Empty/whitespace normalized names not added.
- [MED] 12 Duplicate normalized names → TryAdd keeps first, no throw.
- [MED] 7 Not logged in and login fails → returns early, no /myshows/list/ GET, _shows empty.
- [LOW] 13 HTTP error → caught, logged, no partial corruption of _shows.

7. MyEpisodesClient — NormalizeShowName

- [HIGH] 17 Show resolved from cache → no /search/ request issued.
- [HIGH] 14 _shows empty on entry → PopulateShowsAsync (/myshows/list/) invoked before cache lookup.
- [HIGH] 15 _shows already populated → PopulateShowsAsync not called again (Count==0 guard).
- [MED] 18 Exact match on normalized base name (from cache) → returns id.
- [MED] 19 Exact match on "name (year)" when productionYear given.
- [MED] 20 Partial match + year, single hit → returns it.
- [MED] 21 Partial match + year, multiple hits → returns first.
- [MED] 22 Partial match name-only, single hit → returns it.
- [MED] 23 Partial match name-only, multiple hits → returns first.
- [MED] 24 Not in cache → POSTs /search/ with tvshow + action=Search.
- [MED] 25 Search returns zero results → returns null.
- [MED] 26 Search exact match (single) → calls AddShowAsync, returns id.
- [MED] 27 Search exact match with year (single) → adds + returns.
- [MED] 28 Search partial+year single/multiple → adds + returns (first on multiple).
- [MED] 29 Multiple exact matches → picks first exact, adds + returns.
- [MED] 30 Partial name-only single/multiple → adds + returns (first on multiple).
- [MED] 31 No precise match → falls back to first search result, adds + returns.
- [MED] 33 Null/empty showName → returns null immediately, no network.
- [MED] 34 Not logged in / login fails → returns null.
- [LOW] 35 showName normalizing to empty → returns null.
- [LOW] 32 Search throws → caught, returns null.
- [LOW] 36 Very short normalized name that substring-matches many cached shows → assert deterministic
  (currently first). Documents the over-eager-match risk.

▎ Note: existing year-disambiguation tests (Doctor Who 2005 vs 1963) cover 19–23 via the search path.
▎ Add cache-backed equivalents using WithMyShowsListResponse, since the cache path is where the bug lived.

3. MyEpisodesClient — AddShowAsync  (idempotency guard)

11. MyEpisodesTracker — GetClientForUser (client caching)
- [HIGH] 38 showId not tracked → POSTs action=add + showid, then calls PopulateShowsAsync to refresh.
- [HIGH] 41 Empty _shows → guard's ContainsValue is false → add proceeds (documents guard depends on warm cache).
- [LOW] 39 Guard read happens under lock (_shows) (assert via concurrency test or design review).
- [LOW] 40 HTTP error on add → caught, logged, no throw.

4. MyEpisodesClient — SetEpisodeWatchedStateAsync  (the actual "mark watched" op — untested today)

- [HIGH] 42 watched=true → POSTs key V{showId}-{season}-{episode}=true to eps_update.
- [HIGH] 43 watched=false → POSTs ...=false (lowercase). Verifies the unwatch path really sends false.
- [HIGH] 47 Success (2xx) → returns true.
- [HIGH] 45 Not logged in / login fails → returns false, no POST.
- [MED] 44 Correct headers: Accept, Referer=/show/id-{showId}/, X-Requested-With, Origin.
- [MED] 46 Non-2xx → EnsureSuccessStatusCode throws → caught → returns false.
- [MED] 48 Exception during send → caught, returns false.
- [LOW] 49 200 with empty/logged-out JSON → logs a warning (visibility only; no re-login — per design decision).

5. MyEpisodesClient — EnsureLoggedInAsync

- [MED] 1 First call, valid creds → POSTs /login/ with username,password,action=Login,u="";
  response has /logout/ marker + username → returns true, sets IsLoggedIn.
- [MED] 2 Already logged in → returns true with no second /login/ POST.
- [MED] 3 Login fails (no logout marker) → returns false, IsLoggedIn stays false.
- [MED] 6 HTML has username but no /logout/ link → treated as not-logged-in (stricter check).
- [LOW] 4 Login throws → caught, returns false, no propagation.
- [LOW] 5 Non-success status on /login/ → caught → returns false.

6. MyEpisodesClient — PopulateShowsAsync

- [HIGH] 9 _shows.Clear() before refill → calling twice does not accumulate stale entries.
- [MED] 8 Successful fetch → parses a[href^='/epsbyshow/'], populates normalized name → id.
- [MED] 10 Malformed hrefs (missing/non-numeric id) skipped without throwing.
- [MED] 11 Empty/whitespace normalized names not added.
- [MED] 12 Duplicate normalized names → TryAdd keeps first, no throw.
- [MED] 7 Not logged in and login fails → returns early, no /myshows/list/ GET, _shows empty.
- [LOW] 13 HTTP error → caught, logged, no partial corruption of _shows.

7. MyEpisodesClient — NormalizeShowName

- [LOW] 50 Lowercases input.
- [LOW] 51 Replaces [ ] _ ( ) . - with spaces.
- [LOW] 52 Collapses multiple spaces and trims.
- [LOW] 53 Null/empty → returns empty string.
- [LOW] 54 Punctuation-only → normalizes to empty.
- [LOW] 55 Year in parens handled consistently ("Show (2019)" → "show 2019").

8. MyEpisodesTracker — year source  (wrong-show-match bug)

- [HIGH] 68 Uses series production year (episode.Series?.ProductionYear), NOT episode air year.
  Include a case where episode year ≠ series year and assert the value passed to FindShowIdAsync.

9. MyEpisodesTracker — sync flow

- [MED] 69 FindShowIdAsync returns null → logs warning, IsSuccess=false, SetEpisodeWatchedStateAsync not called.
- [MED] 70 Find + set both succeed → TrackingCompleted fires IsSuccess=true.
- [MED] 71 Find succeeds, set fails → IsSuccess=false.
- [MED] 72 Exception in background task → caught, IsSuccess=false + exception passed.
- [MED] 73 e.UserData.Played passed through as watched flag (true and false).

10. MyEpisodesTracker — OnUserDataSaved (event filtering)

- [LOW] 59 Non-Episode item → no sync, IsSuccess=false.
- [LOW] 60 SaveReason not in {TogglePlayed,PlaybackFinished} → no sync.
- [LOW] 61 SaveReason = TogglePlayed and PlaybackFinished → proceeds.
- [LOW] 62 Null Plugin.Instance.Configuration → no sync.
- [LOW] 63 No matching UserConfiguration → no sync.
- [LOW] 64 Match by both userId.ToString("N") and plain userId.ToString().
- [LOW] 65 SyncWatched=false → no sync.
- [LOW] 66 Empty username or password → no sync.
- [LOW] 67 Missing metadata (null series/season/episode) → logs warning, IsSuccess=false, no sync.

11. MyEpisodesTracker — GetClientForUser (client caching)

- [MED] 74 Same user, unchanged creds → returns same cached client instance.
- [MED] 75 Same user, changed username/password → disposes old, creates + caches new.
- [MED] 76 Different users → separate client instances.
- [MED] 77 StopAsync disposes all clients, clears dict, unsubscribes from UserDataSaved.
- [LOW] 78 Dispose disposes all clients + unsubscribes; no double-dispose issues with StopAsync.

12. MyEpisodesTracker — lifecycle & concurrency

- [LOW] 81 StartAsync subscribes to UserDataSaved.
- [LOW] 82 After StopAsync, a UserDataSaved event triggers no sync (unsubscribed).
- [LOW] 79 Marking several episodes of one series in quick succession → same client, no crash, each
  eps_update sent (documents login-stampede window).
- [LOW] 80 Concurrent syncs with empty _shows → idempotency guard prevents a data-wiping double-add
  of an already-tracked show.

13. MyEpisodesClient — UpdateEpisodeStatus (replaces SetEpisodeWatchedStateAsync)
- [HIGH] Acquired   → POSTs key `A{showId}-{season}-{episode}=true`.
- [HIGH] Unacquired → POSTs key `A{showId}-{season}-{episode}=false`.
- [HIGH] Watched    → POSTs key `V{showId}-{season}-{episode}=true`.
- [HIGH] Unwatched  → POSTs key `V{showId}-{season}-{episode}=false`.
- [MED]  Correct headers (Accept, Referer=/show/id-{showId}/, X-Requested-With, Origin) for all statuses.
- [MED]  Not logged in / login fails → returns false, no POST.
- [MED]  Non-2xx → returns false. Exception during send → returns false.
- [LOW]  Invalid/undefined enum value → throws ArgumentOutOfRangeException (guard the switch).

14. MyEpisodesTracker — OnLibraryItemAdded (acquired sync, all users)
- [HIGH] **Per-user client correctness (regression):** with ≥2 users having SyncAcquired=true,
  each user's episode is synced using THAT user's client — assert `GetClientForUser` is called
  once per distinct userConfig, NOT always the first. (Guards the `.First()` bug.)
- [HIGH] Each qualifying user gets an `A{id}-{s}-{e}=true` (Acquired) eps_update against their own session.
- [HIGH] Non-Episode item → no sync for any user (and decide: should it even fire TrackingCompleted?).
- [HIGH] Missing metadata (null series/season/episode) → logs warning, no sync, IsSuccess=false.
- [MED]  Null Plugin.Instance.Configuration → no sync.
- [MED]  User filtering: only users with SyncAcquired=true AND non-empty JellyfinUserId/Username/Password
  are included. Users missing any are excluded.
- [MED]  Zero qualifying users → logs info, no Task spawned, IsSuccess=false.
- [MED]  FindOrAddShowAsync returns null for a user → that user contributes success=false; other users
  still processed (loop continues, no early return).
- [MED]  Mixed outcomes: user A succeeds, user B fails → aggregatedSuccess=false.
- [MED]  All users succeed → IsSuccess=true, Exception=null.
- [MED]  One user throws → exception captured, other users still processed, IsSuccess=false,
  Exception is that single exception (not AggregateException).
- [MED]  Two+ users throw → Exception is an AggregateException wrapping all.
- [LOW]  SyncAcquired=false but SyncWatched=true users are NOT included in acquired sync.
- [LOW]  Show resolution is attempted per user even for the same series (documents the no-dedup behavior;
  if a per-series cache/throttle is added later, update this).
- [LOW]  Library-scan volume: N episodes of one series added → assert no crash and each fires;
  document that resolution currently repeats per episode.

15. Configuration — SyncAcquired flag
- [MED] MyEpisodesUserConfiguration.SyncAcquired default value is correct (assert default false unless intended).
- [LOW] SyncAcquired and SyncWatched are independent (one on, other off → only that path acts).

16. Regression guard — Watched path unaffected
- [MED] OnUserDataSaved still uses per-user client (GetClientForUser(userConfig)) and Watched/Unwatched
  mapping — re-run section 4/9 cases against the new UpdateEpisodeStatus signature.

  ---
Implementation notes for the agent

- Plugin.Instance is a static singleton — provide a set/reset hook for tracker tests.
- Tracker collaborators (FindShowIdAsync/AddShowAsync) aren't behind interfaces. For "was X called /
  not called" assertions, prefer asserting on the stubbed HTTP layer (no show_manage / /search/
  request sent) rather than mocking the client.
- With MockBehavior.Strict, an unexpected request throws — use that to your advantage for the
  never-called assertions (16, 17, 37), but stub every request a happy path legitimately makes.
- AddShowAsync calls PopulateShowsAsync (hits /myshows/list/) after adding — stub it in add tests.
- Implement [HIGH] first (they cover the data-wipe re-add, the HttpClient leak, the series-year
  mismatch, and the untested watched-state write), then [MED], then [LOW].
