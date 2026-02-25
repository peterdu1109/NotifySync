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
            bool lockTaken = false;
            try
            {
                Monitor.TryEnter(_refreshLock, TimeSpan.FromSeconds(5), ref lockTaken);
                if (!lockTaken)
                {
                    return StatusCode(StatusCodes.Status503ServiceUnavailable, "Système occupé.");
                }

                if ((DateTime.UtcNow - new DateTime(_lastRefreshTime)).TotalSeconds < 60)
                {
                    return StatusCode(429, "Veuillez attendre 1 minute entre chaque rafraîchissement.");
                }

                _lastRefreshTime = DateTime.UtcNow.Ticks;
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
                UserViewCache.Clear();
                Task.Run(() => NotificationManager.Instance.ManualHistoryScan(null!, CancellationToken.None));
                return Ok("Refresh started");
            }

            return NotFound();
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
                var user = (dynamic?)_userManager.GetUserById(Guid.Parse(userId));
                if (user == null)
                {
                    return NotFound();
                }

                var filtered = allNotifs.Where(n =>
                {
                    var item = _libraryManager.GetItemById(n.Id);
                    return item != null && item.IsVisible(user);
                }).ToList();

                long lastSeen = GetUserLastSeen(userId);
                var result = new
                {
                    Hash = hash,
                    LastSeen = lastSeen,
                    Notifications = filtered
                };

                byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(result, PluginJsonContext.Default.Object);
                UserViewCache.TryAdd(cacheKey, serialized);

                return new FileContentResult(serialized, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting data for user {UserId}", userId);
                return StatusCode(500);
            }
        }

        /// <summary>
        /// Sets the last seen timestamp for a user.
        /// </summary>
        /// <param name="userId">The user identifier.</param>
        /// <param name="timestamp">The timestamp.</param>
        /// <returns>An ActionResult indicating the status.</returns>
        [HttpPost("Seen")]
        public ActionResult SetSeen([FromQuery] string userId, [FromQuery] long timestamp)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return BadRequest();
            }

            if (!IsAuthorizedForUser(userId))
            {
                return Forbid();
            }

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

        private bool IsAuthorizedForUser(string userId)
        {
            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(currentUserId))
            {
                return false;
            }

            if (currentUserId == userId)
            {
                return true;
            }

            var currentUser = (dynamic?)_userManager.GetUserById(Guid.Parse(currentUserId));
            return currentUser?.Policy.IsAdministrator == true;
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