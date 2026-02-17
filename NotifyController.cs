using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.IO;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Querying;
using System.Linq;

namespace NotifySync
{
    [ApiController]
    [Route("NotifySync")]
    [Authorize]
    public class NotifyController : ControllerBase
    {
        private readonly IUserManager _userManager;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserDataManager _userDataManager;
        private readonly ILogger<NotifyController> _logger;
        
        // Cache: UserId -> (ServerVersionHash, FilteredList, ETag)
        private static readonly ConcurrentDictionary<Guid, (string Version, List<NotificationItem> Items, string ETag)> _userViewCache = new();
        
        // Case-insensitive dictionary for User IDs (crucial for Linux/Reverse Proxy compatibility)
        private static ConcurrentDictionary<string, string>? _userLastSeenCache;
        private static readonly Lock _fileLock = new();

        private static DateTime _lastRefreshTime = DateTime.MinValue;
        private static readonly Lock _refreshLock = new();

        public NotifyController(IUserManager userManager, ILibraryManager libraryManager, IUserDataManager userDataManager, ILogger<NotifyController> logger)
        {
            _userManager = userManager;
            _libraryManager = libraryManager;
            _userDataManager = userDataManager;
            _logger = logger;
        }

        /// <summary>
        /// Verifies the authenticated user is authorized to access data for the given userId.
        /// Returns true if the authenticated user matches the requested userId, or if the user is an administrator.
        /// </summary>
        private bool IsAuthorizedForUser(string userId)
        {
            if (string.IsNullOrEmpty(userId)) return false;

            // Get the authenticated user's ID from the claims
            var authUserId = HttpContext.User.FindFirst("Jellyfin-UserId")?.Value;
            if (string.IsNullOrEmpty(authUserId)) return false;

            // Allow if the authenticated user matches the requested user
            if (string.Equals(authUserId, userId, StringComparison.OrdinalIgnoreCase)) return true;

            // Allow if this is an API key request
            var isApiKey = HttpContext.User.FindFirst("Jellyfin-IsApiKey")?.Value;
            if (string.Equals(isApiKey, "true", StringComparison.OrdinalIgnoreCase)) return true;

            // Allow if the authenticated user is an admin (check claims)
            var isAdmin = HttpContext.User.FindFirst("Jellyfin-IsAdministrator")?.Value;
            if (string.Equals(isAdmin, "true", StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        [HttpPost("Refresh")]
        public ActionResult Refresh()
        {
            lock (_refreshLock)
            {
                if ((DateTime.UtcNow - _lastRefreshTime).TotalSeconds < 60)
                {
                    return StatusCode(429, "Veuillez attendre 1 minute entre chaque rafraîchissement.");
                }
                _lastRefreshTime = DateTime.UtcNow;
            }

            if (NotificationManager.Instance != null)
            {
                // Invalidate all user caches on manual refresh just to be safe/clean
                _userViewCache.Clear();
                Task.Run(() => NotificationManager.Instance.Refresh());
                return Ok("Refresh started");
            }
            return NotFound();
        }

        [HttpGet("Data")]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)] 
        public ActionResult GetData([FromQuery] string userId)
        {
            if (NotificationManager.Instance == null) return Ok(Array.Empty<object>());
            if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var userGuid)) return BadRequest("Invalid UserId");

            // IDOR protection: verify authenticated user matches requested userId
            if (!IsAuthorizedForUser(userId)) return Forbid();

            // Security check: Use IUserManager to verify user exists
            var user = _userManager.GetUserById(userGuid);
            if (user == null) return NotFound("User not found");

            var currentGlobalVersion = NotificationManager.Instance.GetVersionHash();

            // 1. Try to get from Cache
            if (_userViewCache.TryGetValue(userGuid, out var cached))
            {
                // Verify if the cache is still valid (Server Version is same)
                if (cached.Version == currentGlobalVersion)
                {
                    // Check Client ETag against our Cached ETag
                    if (Request.Headers.TryGetValue("If-None-Match", out var clientTag))
                    {
                        if (clientTag.ToString() == cached.ETag) return StatusCode(304);
                    }
                    
                    Response.Headers["ETag"] = cached.ETag;
                    return Ok(cached.Items);
                }
            }

            // 2. Cache Miss or Stale -> Re-calculate
            var rawNotifications = NotificationManager.Instance.GetRecentNotifications();
            
            // Extract all IDs from notifications to query against User permissions
            var allIds = new List<Guid>(rawNotifications.Count);
            foreach (var n in rawNotifications)
            {
                if (Guid.TryParse(n.Id, out var g)) allIds.Add(g);
            }

            // Bulk Query: Ask Jellyfin Core which of these IDs the user is allowed to see.
            // This handles Libraries, Parental Ratings, Tags, Blocks, etc. efficiently (O(1) query set).
            var query = new InternalItemsQuery(user)
            {
                ItemIds = allIds.ToArray(),
                EnableTotalRecordCount = false
            };
            
            var allowedItems = _libraryManager.GetItemList(query);
            var allowedIdSet = new HashSet<Guid>(allowedItems.Count);
            foreach (var item in allowedItems)
            {
                allowedIdSet.Add(item.Id);
            }

            var filteredNotifications = new List<NotificationItem>(rawNotifications.Count);
            foreach (var notif in rawNotifications)
            {
                // Only return notifications for items that were returned by the secure query
                if (Guid.TryParse(notif.Id, out var id) && allowedIdSet.Contains(id))
                {
                    filteredNotifications.Add(notif);
                }
            }

            // Generate ETag based on Content + Version
            var filteredHash = filteredNotifications.Count > 0 
                ? currentGlobalVersion + "-" + filteredNotifications[0].DateCreated.Ticks + "-" + filteredNotifications.Count 
                : "empty-" + currentGlobalVersion;
            
            // Update Cache
            var cacheEntry = (currentGlobalVersion, filteredNotifications, filteredHash);
            _userViewCache[userGuid] = cacheEntry;

            // 3. Return Response (checking ETag again for the fresh result)
            if (Request.Headers.TryGetValue("If-None-Match", out var cTag))
            {
                if (cTag.ToString() == filteredHash) return StatusCode(304);
            }

            Response.Headers["ETag"] = filteredHash;
            return Ok(filteredNotifications);
        }

        [HttpPost("BulkUserData")]
        public ActionResult GetBulkUserData([FromBody] List<string> itemIds, [FromQuery] string userId)
        {
            if (string.IsNullOrEmpty(userId) || itemIds is null || itemIds.Count == 0 || !Guid.TryParse(userId, out var userGuid))
            {
                return Ok(new Dictionary<string, bool>());
            }

            // IDOR protection: verify authenticated user matches requested userId
            if (!IsAuthorizedForUser(userId)) return Forbid();

            var user = _userManager.GetUserById(userGuid); 
            if (user == null) return Ok(new Dictionary<string, bool>());

            var result = new Dictionary<string, bool>(itemIds.Count);
            var queryIds = new List<Guid>();

            foreach (var idStr in itemIds)
            {
                result[idStr] = false;
                if (Guid.TryParse(idStr, out var guid)) queryIds.Add(guid);
            }

            if (queryIds.Count > 0)
            {
                var query = new InternalItemsQuery(user)
                {
                    ItemIds = queryIds.ToArray(),
                    EnableTotalRecordCount = false
                };

                // Bulk query allowed items
                var allowedItems = _libraryManager.GetItemList(query);
                var allowedItemMap = allowedItems.ToDictionary(i => i.Id);

                foreach (var idStr in itemIds)
                {
                    if (Guid.TryParse(idStr, out var guid) && allowedItemMap.TryGetValue(guid, out var item))
                    {
                        // Permission granted (item is in allowed list). Check UserData.
                        var userData = _userDataManager.GetUserData(user, item);
                        if (userData is not null)
                        {
                            if (userData.Played)
                            {
                                result[idStr] = true;
                            }
                            else if (item.RunTimeTicks > 0 && userData.PlaybackPositionTicks > (item.RunTimeTicks.Value * 0.9))
                            {
                                result[idStr] = true;
                            }
                        }
                    }
                }
            }
            return Ok(result);
        }

        [HttpGet("LastSeen/{userId}")]
        public ActionResult GetLastSeen(string userId)
        {
            if (string.IsNullOrEmpty(userId)) return BadRequest();

            // Simple validation that it's a GUID, though we treat it as string key
            if (!Guid.TryParse(userId, out _)) return BadRequest();

            // IDOR protection: verify authenticated user matches requested userId
            if (!IsAuthorizedForUser(userId)) return Forbid();

            var data = GetCachedUserData();
            if (data.TryGetValue(userId, out var date)) return Ok(date);
            return Ok("2000-01-01T00:00:00.000Z");
        }

        [HttpPost("LastSeen/{userId}")]
        public ActionResult SetLastSeen(string userId, [FromQuery] string date)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(date)) return BadRequest();
            if (!Guid.TryParse(userId, out _)) return BadRequest();

            // IDOR protection: verify authenticated user matches requested userId
            if (!IsAuthorizedForUser(userId)) return Forbid();
            
            var data = GetCachedUserData();
            data.AddOrUpdate(userId, date, (_, _) => date);
            
            // Fire and forget save
            Task.Run(() => 
            {
                lock (_fileLock)
                {
                    SaveUserDataToDisk(data);
                }
            });

            return Ok();
        }

        private ConcurrentDictionary<string, string> GetCachedUserData()
        {
            if (_userLastSeenCache == null)
            {
                lock (_fileLock)
                {
                    if (_userLastSeenCache == null)
                    {
                        var path = Path.Combine(Plugin.Instance!.DataFolderPath, "user_data.json");
                        if (!System.IO.File.Exists(path)) 
                        {
                            _userLastSeenCache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        }
                        else
                        {
                            try 
                            {
                                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                                var dict = JsonSerializer.Deserialize(fs, ControllerJsonContext.Default.DictionaryStringString);
                                _userLastSeenCache = new ConcurrentDictionary<string, string>(dict ?? [], StringComparer.OrdinalIgnoreCase);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Erreur chargement user_data.json");
                                _userLastSeenCache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            }
                        }
                    }
                }
            }
            return _userLastSeenCache;
        }

        private void SaveUserDataToDisk(ConcurrentDictionary<string, string> data)
        {
            try {
                var path = Path.Combine(Plugin.Instance!.DataFolderPath, "user_data.json");
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var tempPath = path + ".tmp";
                var dictToSave = new Dictionary<string, string>(data, StringComparer.OrdinalIgnoreCase);
                
                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(fs, dictToSave, ControllerJsonContext.Default.DictionaryStringString);
                }
                
                System.IO.File.Move(tempPath, path, true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur sauvegarde user_data.json");
            }
        }

        private static byte[]? _clientJsCache;
        private static readonly Lock _jsLock = new();

        [HttpGet("Client.js")]
        [Produces("application/javascript")]
        [AllowAnonymous]
        public ActionResult GetScript()
        {
            if (_clientJsCache == null)
            {
                lock (_jsLock)
                {
                    if (_clientJsCache == null)
                    {
                        var assembly = typeof(NotifyController).Assembly;
                        using var stream = assembly.GetManifestResourceStream("NotifySync.client.js");
                        if (stream == null) return NotFound();
                        
                        using var ms = new MemoryStream();
                        stream.CopyTo(ms);
                        _clientJsCache = ms.ToArray();
                    }
                }
            }
            return File(_clientJsCache, "application/javascript");
        }
    }

    [JsonSerializable(typeof(Dictionary<string, string>))]
    [JsonSerializable(typeof(Dictionary<string, bool>))]
    internal partial class ControllerJsonContext : JsonSerializerContext { }
}