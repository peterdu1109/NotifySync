using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace NotifySync
{
    /// <summary>
    /// API Controller for NotifySync notifications.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("NotifySync")]
    public class NotifyController : ControllerBase
    {
        private static readonly ConcurrentDictionary<string, byte[]> UserViewCache = new ();
        private static readonly ConcurrentDictionary<string, long> UserActionThrottle = new ();
        private static readonly Lazy<string?> _clientJsLazy = new (() =>
        {
            var assembly = typeof(NotifyController).Assembly;
            const string ResourceName = "NotifySync.client.js";
            using var stream = assembly.GetManifestResourceStream(ResourceName);
            if (stream == null)
            {
                return null;
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        });

        private static long _lastRefreshTime;
        private static ILogger<NotifyController>? _staticLogger;

        private readonly IUserManager _userManager;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserDataManager _userDataManager;
        private readonly ILogger<NotifyController> _logger;
        private static readonly object _refreshLock = new ();

        /// <summary>
        /// Initializes a new instance of the <see cref="NotifyController"/> class.
        /// </summary>
        /// <param name="userManager">The user manager.</param>
        /// <param name="libraryManager">The library manager.</param>
        /// <param name="userDataManager">The user data manager.</param>
        /// <param name="logger">The logger.</param>
        public NotifyController(
            IUserManager userManager,
            ILibraryManager libraryManager,
            IUserDataManager userDataManager,
            ILogger<NotifyController> logger)
        {
            _userManager = userManager;
            _libraryManager = libraryManager;
            _userDataManager = userDataManager;
            _logger = logger;
            Interlocked.CompareExchange(ref _staticLogger, logger, null);
        }

        /// <summary>
        /// Triggers a manual refresh of the notification history.
        /// </summary>
        /// <returns>An ActionResult indicating the status.</returns>
        [HttpPost("Refresh")]
        public ActionResult Refresh()
        {
            if (!User.IsInRole("Administrator")
                && !string.Equals(User.FindFirst("Jellyfin-IsApiKey")?.Value, "true", StringComparison.OrdinalIgnoreCase))
            {
                return Forbid();
            }

            _logger.LogInformation("NotifySync: Manual refresh requested from the interface.");
            bool lockTaken = false;
            try
            {
                Monitor.TryEnter(_refreshLock, TimeSpan.FromSeconds(5), ref lockTaken);
                if (!lockTaken)
                {
                    _logger.LogWarning("NotifySync: Refresh skipped, lock is busy.");
                    return StatusCode(StatusCodes.Status503ServiceUnavailable, "System busy.");
                }

                var now = DateTime.UtcNow;
                if ((now - new DateTime(_lastRefreshTime)).TotalSeconds < 30)
                {
                    _logger.LogWarning("NotifySync: Refresh rate-limited.");
                    return StatusCode(429, "Please wait 30 seconds.");
                }

                _lastRefreshTime = now.Ticks;
            }
            finally
            {
                if (lockTaken)
                {
                    Monitor.Exit(_refreshLock);
                }
            }

            if (NotificationManager.Instance != null)
            {
                _logger.LogInformation("NotifySync: Starting manual history scan...");
                UserViewCache.Clear();
                _ = Task.Run(() =>
                {
                    try
                    {
                        NotificationManager.Instance!.ManualHistoryScan(new Progress<double>(), CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _staticLogger?.LogError(ex, "NotifySync: Manual history scan failed.");
                    }
                });
                return Ok(new { Message = "Refresh started" });
            }

            _logger.LogError("NotifySync: NotificationManager.Instance is null during refresh!");
            return StatusCode(500, "Manager not initialized.");
        }

        /// <summary>
        /// Triggers a manual scan of monitored collections (BoxSets) for newly added items.
        /// Useful when the admin just added a collection to monitor and doesn't want to
        /// wait for the 15-minute interval trigger of <see cref="CollectionScanTask"/>.
        /// Admin-only and rate-limited like the history refresh endpoint.
        /// </summary>
        /// <returns>An ActionResult indicating the status.</returns>
        [HttpPost("ScanCollections")]
        public ActionResult ScanCollectionsNow()
        {
            if (!User.IsInRole("Administrator")
                && !string.Equals(User.FindFirst("Jellyfin-IsApiKey")?.Value, "true", StringComparison.OrdinalIgnoreCase))
            {
                return Forbid();
            }

            _logger.LogInformation("NotifySync: Manual collection scan requested from the interface.");

            if (NotificationManager.Instance == null)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Manager not initialized.");
            }

            // Tell the caller upfront when no collection is monitored, so the UI can
            // explain "nothing to scan" instead of silently claiming success.
            var enabledCollections = Plugin.Instance?.Configuration?.EnabledCollections;
            if (enabledCollections == null || enabledCollections.Count == 0)
            {
                return Ok(new { Message = "No collections monitored", NoCollections = true });
            }

            _ = Task.Run(() =>
            {
                try
                {
                    NotificationManager.Instance.ScanCollections(new Progress<double>(), CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _staticLogger?.LogError(ex, "NotifySync: Manual collection scan failed.");
                }
            });
            return Ok(new { Message = "Collection scan started" });
        }

        /// <summary>
        /// Serves the client-side script for NotifySync.
        /// </summary>
        /// <returns>The javascript file.</returns>
        [HttpGet("Client.js")]
        [AllowAnonymous]
        [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
        public ActionResult GetClientJs()
        {
            var js = _clientJsLazy.Value;
            if (js == null)
            {
                _logger.LogError("NotifySync: Embedded resource client.js not found!");
                return NotFound();
            }

            return Content(js, "application/javascript");
        }

        /// <summary>
        /// Gets notification data for a specific user.
        /// </summary>
        /// <param name="userId">The user identifier.</param>
        /// <returns>An ActionResult containing the notification data.</returns>
        [HttpGet("Data")]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public ActionResult GetData([FromQuery] string userId)
        {
            if (NotificationManager.Instance == null)
            {
                return Ok(Array.Empty<object>());
            }

            if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out _))
            {
                return BadRequest("Invalid UserId");
            }

            if (!IsAuthorizedForUser(userId))
            {
                _logger.LogWarning("NotifySync: GetData access denied for user {UserId}.", userId);
                return Forbid();
            }

            _logger.LogDebug("NotifySync: GetData requested for {UserId}.", userId);

            try
            {
                var normalizedId = NormalizeId(userId);
                var hash = NotificationManager.Instance.GetVersionHash(normalizedId);

                // ETag 304 support: if client already has this version, skip serialization
                var ifNoneMatch = Request.Headers["If-None-Match"].ToString();
                if (!string.IsNullOrEmpty(ifNoneMatch) && ifNoneMatch == hash)
                {
                    return StatusCode(304);
                }

                string cacheKey = normalizedId + "_" + hash;

                if (UserViewCache.TryGetValue(cacheKey, out var cachedData))
                {
                    Response.Headers["ETag"] = hash;
                    return new FileContentResult(cachedData, "application/json");
                }

                var allNotifs = NotificationManager.Instance.GetRecentNotifications();
                var user = _userManager.GetUserById(Guid.Parse(userId));
                if (user == null)
                {
                    return NotFound();
                }

                // Load per-user read/dismissed states
                var userStates = NotificationManager.Instance.Db.GetUserStates(normalizedId);
                long clearedUntil = NotificationManager.Instance.GetUserCleared(userId);

                var filtered = new List<NotificationItem>();
                int filteredNotVisible = 0;
                int itemNotFound = 0;

                // Phase 1: cheap filters (no DB/library calls) to reduce N+1 impact
                var candidates = new List<NotificationItem>();
                foreach (var n in allNotifs)
                {
                    userStates.TryGetValue(n.Id, out var state);
                    if (state.IsDismissed)
                    {
                        continue;
                    }

                    if (n.DateCreated.ToUniversalTime().Ticks <= clearedUntil)
                    {
                        continue;
                    }

                    candidates.Add(n);
                }

                // Phase 2: library lookups only on remaining candidates
                foreach (var n in candidates)
                {
                    var item = _libraryManager.GetItemById(n.Id);
                    if (item == null && !string.IsNullOrEmpty(n.RealItemId))
                    {
                        // Synthetic ID (e.g. collection notification) — resolve the real Jellyfin item
                        // so we can enforce visibility and played-status checks.
                        item = _libraryManager.GetItemById(n.RealItemId);
                    }

                    if (item == null)
                    {
                        // Item was deleted from Jellyfin but the notification is still in DB.
                        // We cannot enforce the visibility check on a missing item, so showing
                        // the notification would leak its metadata (title, year, series, etc.)
                        // to every user — including those who never had access to that library.
                        // Skip orphans entirely.
                        itemNotFound++;
                        continue;
                    }

                    if (!item.IsVisible(user))
                    {
                        filteredNotVisible++;
                        continue;
                    }

                    var userData = _userDataManager.GetUserData(user, item);
                    if (userData != null && userData.Played && !n.IsUpgrade)
                    {
                        continue;
                    }

                    filtered.Add(n);
                }

                // Clone only the filtered items and apply per-user read state
                var filteredClones = new List<NotificationItem>(filtered.Count);
                foreach (var n in filtered)
                {
                    var clone = n.Clone();
                    userStates.TryGetValue(clone.Id, out var readState);
                    clone.IsRead = readState.IsRead;
                    filteredClones.Add(clone);
                }

                var filteredList = filteredClones.OrderByDescending(n => n.DateCreated).ToList();
                int maxItems = Plugin.Instance?.Configuration?.MaxItems ?? 10;
                var quotaResult = CategoryQuotaService.ApplyCategoryQuotas(filteredList, maxItems);
                filteredList = quotaResult.Kept.ToList();

                _logger.LogDebug(
                    "NotifySync GetData: Total={Total}, NotFound={NotFound}, NotVisible={NotVisible}, Result: {Cats}",
                    allNotifs.Count,
                    itemNotFound,
                    filteredNotVisible,
                    string.Join(", ", filteredList.GroupBy(n => n.Category).Select(g => $"{g.Key}={g.Count()}")));

                byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(filteredList, PluginJsonContext.Default.ListNotificationItem);

                // Evict stale entries for this user before inserting new one
                InvalidateUserCache(normalizedId);
                UserViewCache[cacheKey] = serialized;

                // Purge cache if it grows too large (safety net)
                if (UserViewCache.Count > 500)
                {
                    UserViewCache.Clear();
                    UserViewCache[cacheKey] = serialized;
                }

                Response.Headers["ETag"] = hash;
                return new FileContentResult(serialized, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotifySync: Error retrieving data for user {UserId}.", userId);
                return StatusCode(500, "Internal error.");
            }
        }

        /// <summary>
        /// Gets the last cleared timestamp for a user.
        /// </summary>
        /// <param name="userId">The user identifier.</param>
        /// <returns>An ActionResult containing the last cleared timestamp.</returns>
        [HttpGet("Cleared/{userId}")]
        public ActionResult GetCleared([FromRoute] string userId)
        {
            if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out _))
            {
                return BadRequest("Invalid UserId");
            }

            if (!IsAuthorizedForUser(userId))
            {
                return Forbid();
            }

            long cleared = NotificationManager.Instance?.GetUserCleared(userId) ?? 0;
            return Ok(new DateTime(cleared, DateTimeKind.Utc).ToString("O"));
        }

        /// <summary>
        /// Sets the cleared timestamp for a user.
        /// </summary>
        /// <param name="userId">The user identifier.</param>
        /// <param name="date">The ISO date string (optional, defaults to now).</param>
        /// <returns>An ActionResult indicating the status.</returns>
        [HttpPost("Clear/{userId}")]
        public ActionResult SetCleared([FromRoute] string userId, [FromQuery] string? date)
        {
            if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out _))
            {
                return BadRequest("Invalid UserId");
            }

            if (!IsAuthorizedForUser(userId))
            {
                return Forbid();
            }

            DateTime dt = string.IsNullOrEmpty(date) || !DateTime.TryParse(date, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var parsedDate)
                ? DateTime.UtcNow
                : parsedDate;
            long timestamp = dt.Ticks;

            NotificationManager.Instance?.SetUserCleared(userId, timestamp);
            InvalidateUserCache(NormalizeId(userId));

            return Ok();
        }

        /// <summary>
        /// Gets played status for a list of items.
        /// </summary>
        /// <param name="userId">The user identifier.</param>
        /// <returns>An ActionResult containing a dictionary of item IDs and their played status.</returns>
        [HttpPost("BulkUserData")]
        public async Task<ActionResult> GetBulkUserData([FromQuery] string userId)
        {
            if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out _))
            {
                return BadRequest();
            }

            if (!IsAuthorizedForUser(userId))
            {
                return Forbid();
            }

            try
            {
                using var reader = new StreamReader(Request.Body);
                var body = await reader.ReadToEndAsync().ConfigureAwait(false);
                var itemIds = JsonSerializer.Deserialize(body, PluginJsonContext.Default.ListString);

                if (itemIds == null)
                {
                    return BadRequest();
                }

                var user = _userManager.GetUserById(Guid.Parse(userId));
                if (user == null)
                {
                    return NotFound();
                }

                var results = new Dictionary<string, bool>();
                foreach (var id in itemIds)
                {
                    if (string.IsNullOrEmpty(id))
                    {
                        continue;
                    }

                    var item = _libraryManager.GetItemById(id);
                    if (item != null)
                    {
                        var userData = _userDataManager.GetUserData(user, item);
                        results[id] = userData?.Played ?? false;
                    }
                    else
                    {
                        results[id] = false;
                    }
                }

                byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(results, PluginJsonContext.Default.DictionaryStringBoolean);
                return new FileContentResult(serialized, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotifySync: Error in BulkUserData for user {UserId}.", userId);
                return StatusCode(500, "Internal error.");
            }
        }

        /// <summary>
        /// Marks notifications as read for a user (server-side persistent state).
        /// </summary>
        /// <param name="userId">The user identifier.</param>
        /// <returns>An ActionResult indicating the status.</returns>
        [HttpPost("MarkRead")]
        public async Task<ActionResult> MarkRead([FromQuery] string userId)
        {
            if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out _))
            {
                return BadRequest("Invalid UserId");
            }

            if (!IsAuthorizedForUser(userId))
            {
                return Forbid();
            }

            try
            {
                using var reader = new StreamReader(Request.Body);
                var body = await reader.ReadToEndAsync().ConfigureAwait(false);
                var itemIds = JsonSerializer.Deserialize(body, PluginJsonContext.Default.ListString);
                if (itemIds == null || itemIds.Count == 0)
                {
                    return BadRequest("Empty item list");
                }

                if (NotificationManager.Instance == null)
                {
                    return StatusCode(StatusCodes.Status503ServiceUnavailable, "Manager not initialized.");
                }

                var normalizedUserId = NormalizeId(userId);
                if (IsUserThrottled(normalizedUserId))
                {
                    return StatusCode(429, "Too many requests.");
                }

                NotificationManager.Instance.Db.BulkSetRead(normalizedUserId, itemIds);
                NotificationManager.Instance.IncrementUserStateVersion(normalizedUserId);
                InvalidateUserCache(normalizedUserId);
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotifySync: Error in MarkRead for user {UserId}.", userId);
                return StatusCode(500, "Internal error.");
            }
        }

        /// <summary>
        /// Dismisses a single notification for a user.
        /// </summary>
        /// <param name="userId">The user identifier.</param>
        /// <param name="itemId">The notification item identifier.</param>
        /// <returns>An ActionResult indicating the status.</returns>
        [HttpPost("Dismiss/{userId}/{itemId}")]
        public ActionResult Dismiss([FromRoute] string userId, [FromRoute] string itemId)
        {
            if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out _)
                || string.IsNullOrEmpty(itemId))
            {
                return BadRequest("Invalid UserId or ItemId");
            }

            if (!IsAuthorizedForUser(userId))
            {
                return Forbid();
            }

            if (NotificationManager.Instance == null)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Manager not initialized.");
            }

            var normalizedUserId = NormalizeId(userId);
            if (IsUserThrottled(normalizedUserId))
            {
                return StatusCode(429, "Too many requests.");
            }

            // Resolve group dismiss: if itemId is a SeriesId, find all episode IDs (no clone overhead)
            var idsToDismiss = NotificationManager.Instance.ResolveNotificationIds(itemId);

            foreach (var id in idsToDismiss)
            {
                NotificationManager.Instance.Db.SetItemDismissed(normalizedUserId, id);
            }

            NotificationManager.Instance.IncrementUserStateVersion(normalizedUserId);
            InvalidateUserCache(normalizedUserId);
            return Ok();
        }

        /// <summary>
        /// Dismisses multiple notifications for a user in a single request.
        /// Bypasses the per-item throttle by using a bulk database operation.
        /// </summary>
        /// <param name="userId">The user identifier.</param>
        /// <returns>An ActionResult indicating the status.</returns>
        [HttpPost("BulkDismiss/{userId}")]
        public async Task<ActionResult> BulkDismiss([FromRoute] string userId)
        {
            if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out _))
            {
                return BadRequest("Invalid UserId");
            }

            if (!IsAuthorizedForUser(userId))
            {
                return Forbid();
            }

            if (NotificationManager.Instance == null)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Manager not initialized.");
            }

            try
            {
                using var reader = new StreamReader(Request.Body);
                var body = await reader.ReadToEndAsync().ConfigureAwait(false);
                var itemIds = JsonSerializer.Deserialize(body, PluginJsonContext.Default.ListString);
                if (itemIds == null || itemIds.Count == 0)
                {
                    return BadRequest("Empty item list");
                }

                // Cap to a reasonable maximum to prevent abuse
                if (itemIds.Count > 500)
                {
                    return BadRequest("Too many items (max 500).");
                }

                var normalizedUserId = NormalizeId(userId);

                // Resolve group IDs: each itemId might be a SeriesId that maps to multiple episode IDs
                var allIds = new List<string>();
                foreach (var id in itemIds)
                {
                    if (string.IsNullOrEmpty(id))
                    {
                        continue;
                    }

                    var resolved = NotificationManager.Instance.ResolveNotificationIds(id);
                    allIds.AddRange(resolved);
                }

                if (allIds.Count == 0)
                {
                    return BadRequest("No valid item IDs.");
                }

                NotificationManager.Instance.Db.BulkSetDismissed(normalizedUserId, allIds);
                NotificationManager.Instance.IncrementUserStateVersion(normalizedUserId);
                InvalidateUserCache(normalizedUserId);

                _logger.LogDebug("NotifySync : BulkDismiss {Count} items for user {UserId}.", allIds.Count, userId);
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotifySync: Error in BulkDismiss for user {UserId}.", userId);
                return StatusCode(500, "Internal error.");
            }
        }

        /// <summary>
        /// Gets all read/dismissed states for a user.
        /// </summary>
        /// <param name="userId">The user identifier.</param>
        /// <returns>An ActionResult containing a dictionary of notification states.</returns>
        [HttpGet("UserStates/{userId}")]
        public ActionResult GetUserStates([FromRoute] string userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return BadRequest();
            }

            if (!IsAuthorizedForUser(userId))
            {
                return Forbid();
            }

            var normalizedUserId = NormalizeId(userId);
            var states = NotificationManager.Instance?.Db.GetUserStates(normalizedUserId)
                ?? new Dictionary<string, (bool IsRead, bool IsDismissed)>();

            // Flatten to AOT-compatible Dictionary<string, bool> with composite keys
            var result = new Dictionary<string, bool>();
            foreach (var kvp in states)
            {
                result[kvp.Key + ":isRead"] = kvp.Value.IsRead;
                result[kvp.Key + ":isDismissed"] = kvp.Value.IsDismissed;
            }

            byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(result, PluginJsonContext.Default.DictionaryStringBoolean);
            return new FileContentResult(serialized, "application/json");
        }

        /// <summary>
        /// Gets the list of recently deleted items (admin only, shown in config page).
        /// </summary>
        /// <param name="limit">Maximum number of records to return.</param>
        /// <param name="offset">Number of records to skip.</param>
        /// <returns>An ActionResult containing the list of deleted items.</returns>
        [HttpGet("DeletedItems")]
        public ActionResult GetDeletedItems([FromQuery] int limit = 200, [FromQuery] int offset = 0)
        {
            if (!User.IsInRole("Administrator")
                && !string.Equals(User.FindFirst("Jellyfin-IsApiKey")?.Value, "true", StringComparison.OrdinalIgnoreCase))
            {
                return Forbid();
            }

            // Cap parameters to safe bounds
            limit = Math.Clamp(limit, 1, 500);
            offset = Math.Max(offset, 0);

            if (NotificationManager.Instance == null)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Manager not initialized.");
            }

            var items = NotificationManager.Instance.Db.GetDeletedItems(limit, offset);
            byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(items, PluginJsonContext.Default.ListDeletedItemRecord);
            return new FileContentResult(serialized, "application/json");
        }

        private static string NormalizeId(string userId) => IdHelper.NormalizeId(userId);

        private bool IsAuthorizedForUser(string userId)
        {
            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(currentUserId))
            {
                currentUserId = User.FindFirst("UserId")?.Value ?? User.Identity?.Name;
            }

            if (string.IsNullOrEmpty(currentUserId))
            {
                _logger.LogWarning("NotifySync: No user identity found in the request principal.");
                return false;
            }

            if (!Guid.TryParse(currentUserId, out var currentGuid))
            {
                var userByName = _userManager.GetUserByName(currentUserId);
                if (userByName != null)
                {
                    currentGuid = userByName.Id;
                    currentUserId = currentGuid.ToString("N");
                }
                else
                {
                    _logger.LogWarning("NotifySync: Unable to resolve username '{Username}'.", currentUserId);
                    return false;
                }
            }

            var normalizedCurrent = NormalizeId(currentUserId);
            var normalizedRequested = NormalizeId(userId);

            if (normalizedCurrent == normalizedRequested)
            {
                return true;
            }

            var isAdmin = User.IsInRole("Administrator")
                || string.Equals(User.FindFirst("Jellyfin-IsApiKey")?.Value, "true", StringComparison.OrdinalIgnoreCase);
            if (isAdmin)
            {
                return true;
            }

            _logger.LogWarning("NotifySync: Authorization denied. Current: {Current}, Requested: {Requested}.", currentUserId, userId);
            return false;
        }

        private static bool IsUserThrottled(string userId, int minIntervalMs = 500)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            bool throttled = false;
            UserActionThrottle.AddOrUpdate(
                userId,
                now,
                (_, last) =>
                {
                    if ((now - last) < minIntervalMs)
                    {
                        throttled = true;
                        return last;
                    }

                    return now;
                });

            // Periodic purge: remove stale entries older than 60 seconds
            if (UserActionThrottle.Count > 100)
            {
                foreach (var kvp in UserActionThrottle.ToList())
                {
                    if ((now - kvp.Value) > 60_000)
                    {
                        UserActionThrottle.TryRemove(kvp.Key, out _);
                    }
                }
            }

            return throttled;
        }

        /// <summary>
        /// Invalidates the view cache for a specific user.
        /// </summary>
        /// <param name="userId">The user identifier.</param>
        internal static void InvalidateUserCache(string userId)
        {
            var prefix = userId + "_";
            foreach (var kvp in UserViewCache.ToList())
            {
                if (kvp.Key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    UserViewCache.TryRemove(kvp.Key, out _);
                }
            }
        }
    }
}
