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
using MediaBrowser.Model.Entities; // Added for BaseItemKind
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
        private static readonly ConcurrentDictionary<string, long> UserLastSeenCache = new ();
        private static readonly object GlobalFileLock = new ();
        private static long _lastRefreshTime;
        private static ILogger<NotifyController>? _staticLogger;

        private readonly IUserManager _userManager;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserDataManager _userDataManager;
        private readonly ILogger<NotifyController> _logger;
        private readonly object _refreshLock = new ();

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
            _staticLogger ??= logger;
        }

        /// <summary>
        /// Triggers a manual refresh of the notification history.
        /// </summary>
        /// <returns>An ActionResult indicating the status.</returns>
        [HttpPost("Refresh")]
        public ActionResult Refresh()
        {
            _logger.LogInformation("NotifySync: Manual refresh requested from UI.");
            bool lockTaken = false;
            try
            {
                Monitor.TryEnter(_refreshLock, TimeSpan.FromSeconds(5), ref lockTaken);
                if (!lockTaken)
                {
                    _logger.LogWarning("NotifySync: Refresh ignored, lock busy.");
                    return StatusCode(StatusCodes.Status503ServiceUnavailable, "Système occupé.");
                }

                var now = DateTime.UtcNow;
                if ((now - new DateTime(_lastRefreshTime)).TotalSeconds < 30)
                {
                    _logger.LogWarning("NotifySync: Refresh rate limited.");
                    return StatusCode(429, "Veuillez attendre 30 secondes.");
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
                Task.Run(() => NotificationManager.Instance.ManualHistoryScan(null!, CancellationToken.None));
                return Ok(new { Message = "Refresh started" });
            }

            _logger.LogError("NotifySync: NotificationManager.Instance is null during refresh!");
            return StatusCode(500, "Manager non initialisé.");
        }

        /// <summary>
        /// Serves the client-side script for NotifySync.
        /// </summary>
        /// <returns>The javascript file.</returns>
        [HttpGet("Client.js")]
        [AllowAnonymous]
        [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
        public ActionResult GetClientJs()
        {
            var assembly = GetType().Assembly;
            const string ResourceName = "NotifySync.client.js";
            using var stream = assembly.GetManifestResourceStream(ResourceName);
            if (stream == null)
            {
                _logger.LogError("NotifySync: client.js resource not found! Expected: {ResourceName}", ResourceName);
                return NotFound();
            }

            using var reader = new StreamReader(stream);
            return Content(reader.ReadToEnd(), "application/javascript");
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
                _logger.LogWarning("GetData denied for user {UserId}", userId);
                return Forbid();
            }

            _logger.LogDebug("GetData requested for {UserId}", userId);

            try
            {
                var hash = NotificationManager.Instance.GetVersionHash();
                string cacheKey = userId + "_" + hash;

                if (UserViewCache.TryGetValue(cacheKey, out var cachedData))
                {
                    return new FileContentResult(cachedData, "application/json");
                }

                var allNotifs = NotificationManager.Instance.GetRecentNotifications();
                var user = _userManager.GetUserById(Guid.Parse(userId));
                if (user == null)
                {
                    return NotFound();
                }

                var filtered = new List<NotificationItem>();
                int filteredNotVisible = 0;
                int itemNotFound = 0;
                foreach (var n in allNotifs)
                {
                    var item = _libraryManager.GetItemById(n.Id);
                    if (item == null)
                    {
                        // Item not in native Jellyfin DB (e.g. IPTV/external provider).
                        // Keep it — it was validated during the scan phase.
                        itemNotFound++;
                        filtered.Add(n);
                        continue;
                    }

                    if (!item.IsVisible(user))
                    {
                        filteredNotVisible++;
                        continue;
                    }

                    filtered.Add(n);
                }

                _logger.LogInformation(
                    "GetData Diagnostics: Total={Total}, NotInLibrary(kept)={NotFound}, FilteredNotVisible={NotVisible}, Result: {Cats}",
                    allNotifs.Count,
                    itemNotFound,
                    filteredNotVisible,
                    string.Join(", ", filtered.GroupBy(n => n.Category).Select(g => $"{g.Key}={g.Count()}")));

                byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(filtered, PluginJsonContext.Default.ListNotificationItem);

                // --- FIX MEMORY LEAK ---
                // Remove all previous cached views for this exact user before adding the new hash.
                var userKeysToRemove = UserViewCache.Keys.Where(k => k.StartsWith(userId + "_", StringComparison.Ordinal)).ToList();
                foreach (var key in userKeysToRemove)
                {
                    UserViewCache.TryRemove(key, out _);
                }

                UserViewCache.TryAdd(cacheKey, serialized);

                Response.Headers["ETag"] = hash;
                return new FileContentResult(serialized, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting data for user {UserId}", userId);
                return StatusCode(500);
            }
        }

        /// <summary>
        /// Gets the last seen timestamp for a user.
        /// </summary>
        /// <param name="userId">The user identifier.</param>
        /// <returns>An ActionResult containing the last seen timestamp.</returns>
        [HttpGet("LastSeen/{userId}")]
        public ActionResult GetLastSeen([FromRoute] string userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return BadRequest();
            }

            if (!IsAuthorizedForUser(userId))
            {
                return Forbid();
            }

            long lastSeen = GetUserLastSeen(userId);
            return Ok(JsonSerializer.Serialize(new DateTime(lastSeen).ToString("O"), PluginJsonContext.Default.Object));
        }

        /// <summary>
        /// Sets the last seen timestamp for a user.
        /// </summary>
        /// <param name="userId">The user identifier.</param>
        /// <param name="date">The ISO date string (optional, defaults to now).</param>
        /// <returns>An ActionResult indicating the status.</returns>
        [HttpPost("LastSeen/{userId}")]
        public ActionResult SetLastSeen([FromRoute] string userId, [FromQuery] string? date)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return BadRequest();
            }

            if (!IsAuthorizedForUser(userId))
            {
                return Forbid();
            }

            DateTime dt = string.IsNullOrEmpty(date) ? DateTime.UtcNow : DateTime.Parse(date, System.Globalization.CultureInfo.InvariantCulture);
            long timestamp = dt.Ticks;

            UserLastSeenCache[userId] = timestamp;
            SaveUserLastSeen(userId, timestamp);

            // Invalidate cache for this user because LastSeen changed
            var keysToRemove = UserViewCache.Keys.Where(k => k.StartsWith(userId, StringComparison.Ordinal)).ToList();
            foreach (var key in keysToRemove)
            {
                UserViewCache.TryRemove(key, out _);
            }

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
#pragma warning disable CS8602
                foreach (var id in itemIds!)
                {
                    if (string.IsNullOrEmpty(id))
                    {
                        continue;
                    }

                    var item = _libraryManager.GetItemById(id);
                    if (item != null)
                    {
                        var userObj = user!;
                        var userData = _userDataManager.GetUserData(userObj, item);
                        results[id] = userData.Played;
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
                _logger.LogError(ex, "Error in BulkUserData for user {UserId}", userId);
                return StatusCode(500);
            }
        }

        private bool IsAuthorizedForUser(string userId)
        {
            // First try NameIdentifier (Standard)
            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            // Fallback to "Name" or "UserId" claims if NameIdentifier is missing
            if (string.IsNullOrEmpty(currentUserId))
            {
                currentUserId = User.FindFirst("UserId")?.Value ?? User.Identity?.Name;
            }

            if (string.IsNullOrEmpty(currentUserId))
            {
                _logger.LogWarning("NotifySync: No user identity found in request principal.");
                return false;
            }

            // If the claim is a username (not a GUID), resolve it to a user GUID
            if (!Guid.TryParse(currentUserId, out var currentGuid))
            {
                // The claim is a username string (e.g. "jellyfin"), look up the user by name
                var userByName = _userManager.GetUserByName(currentUserId);
                if (userByName != null)
                {
                    currentGuid = userByName.Id;
                    currentUserId = currentGuid.ToString("N");
                }
                else
                {
                    _logger.LogWarning("NotifySync: Could not resolve username '{Username}' to a user.", currentUserId);
                    return false;
                }
            }

            // Normalize both IDs for comparison (handle hyphenated vs non-hyphenated formats)
            var normalizedCurrent = currentUserId.Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
            var normalizedRequested = userId.Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase).ToLowerInvariant();

            if (normalizedCurrent == normalizedRequested)
            {
                return true;
            }

            // Check if the current user is an admin (allow admin access to any user's data)
            var isAdmin = User.IsInRole("Administrator")
                || string.Equals(User.FindFirst("Jellyfin-IsApiKey")?.Value, "true", StringComparison.OrdinalIgnoreCase);
            if (isAdmin)
            {
                return true;
            }

            _logger.LogWarning("NotifySync: Authorization denied. Current: {Current}, Requested: {Requested}", currentUserId, userId);
            return false;
        }

        private long GetUserLastSeen(string userId)
        {
            if (UserLastSeenCache.TryGetValue(userId, out var ts))
            {
                return ts;
            }

            bool lockTaken = false;
            try
            {
                Monitor.TryEnter(GlobalFileLock, TimeSpan.FromSeconds(5), ref lockTaken);
                if (!lockTaken)
                {
                    return 0;
                }

                string path = Path.Combine(Plugin.Instance!.DataFolderPath, "users_seen.json");
                if (!System.IO.File.Exists(path))
                {
                    return 0;
                }

                try
                {
                    var json = System.IO.File.ReadAllText(path);
                    var data = JsonSerializer.Deserialize(json, PluginJsonContext.Default.DictionaryStringInt64);
                    if (data != null && data.TryGetValue(userId, out var val))
                    {
                        UserLastSeenCache[userId] = val;
                        return val;
                    }
                }
                catch
                {
                    // Ignore errors during read
                }
            }
            finally
            {
                if (lockTaken)
                {
                    Monitor.Exit(GlobalFileLock);
                }
            }

            return 0;
        }

        private void SaveUserLastSeen(string userId, long timestamp)
        {
            Task.Run(() =>
            {
                bool lockTaken = false;
                try
                {
                    Monitor.TryEnter(GlobalFileLock, TimeSpan.FromSeconds(30), ref lockTaken);
                    if (!lockTaken)
                    {
                        return;
                    }

                    string path = Path.Combine(Plugin.Instance!.DataFolderPath, "users_seen.json");
                    Dictionary<string, long> data;

                    if (System.IO.File.Exists(path))
                    {
                        try
                        {
                            var json = System.IO.File.ReadAllText(path);
                            data = JsonSerializer.Deserialize(json, PluginJsonContext.Default.DictionaryStringInt64) ?? new Dictionary<string, long>();
                        }
                        catch
                        {
                            data = new Dictionary<string, long>();
                        }
                    }
                    else
                    {
                        data = new Dictionary<string, long>();
                    }

                    data[userId] = timestamp;
                    System.IO.File.WriteAllText(path, JsonSerializer.Serialize(data, PluginJsonContext.Default.DictionaryStringInt64));
                }
                catch (Exception ex)
                {
                    _staticLogger?.LogError(ex, "Error saving user seen data");
                }
                finally
                {
                    if (lockTaken)
                    {
                        Monitor.Exit(GlobalFileLock);
                    }
                }
            });
        }
    }
}
