using System;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Net;
using System.Linq; // Nécessaire pour le filtrage

namespace NotifySync
{
    [ApiController]
    [Route("NotifySync")]
    public class NotifyController : ControllerBase
    {
        private readonly IUserManager _userManager;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserDataManager _userDataManager;
        
        private static ConcurrentDictionary<string, string>? _userDataCache;
        private static readonly Lock _fileLock = new();

        // SÉCURITÉ (DoS) : Timestamp pour limiter la fréquence de rafraîchissement
        private static DateTime _lastRefreshTime = DateTime.MinValue;
        private static readonly Lock _refreshLock = new();

        public NotifyController(IUserManager userManager, ILibraryManager libraryManager, IUserDataManager userDataManager)
        {
            _userManager = userManager;
            _libraryManager = libraryManager;
            _userDataManager = userDataManager;
        }

        [HttpPost("Refresh")]
        public ActionResult Refresh()
        {
            // SÉCURITÉ : Rate Limiting (Anti-Spam)
            // On empêche de lancer un refresh si le dernier a eu lieu il y a moins de 60 secondes
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
                // Lance le refresh en arrière-plan pour ne pas bloquer la requête HTTP
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

            var rawNotifications = NotificationManager.Instance.GetRecentNotifications();
            
            // SÉCURITÉ (Privacy) : Filtrage par permissions utilisateur
            // Si un userId est fourni, on filtre les éléments qu'il n'a pas le droit de voir
            if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var userGuid))
            {
                var user = _userManager.GetUserById(userGuid);
                if (user != null)
                {
                    // On ne garde que les items visibles pour cet utilisateur (Parents, Tags, Classification)
                    var filteredNotifications = new List<NotificationItem>(rawNotifications.Count);
                    foreach(var notif in rawNotifications)
                    {
                        if (Guid.TryParse(notif.Id, out var itemId))
                        {
                            var item = _libraryManager.GetItemById(itemId);
                            // item.IsVisible vérifie les droits d'accès Jellyfin natifs
                            if (item != null && item.IsVisible(user))
                            {
                                filteredNotifications.Add(notif);
                            }
                        }
                    }
                    
                    // On recalcule le hash ETag basé sur la liste FILTRÉE
                    // Sinon le cache client pourrait afficher des données périmées ou incorrectes
                    var filteredHash = filteredNotifications.Count > 0 
                        ? filteredNotifications[0].DateCreated.Ticks + "-" + filteredNotifications.Count 
                        : "empty";

                    if (Request.Headers.TryGetValue("If-None-Match", out var clientTag))
                    {
                        if (clientTag.ToString() == filteredHash) return StatusCode(304);
                    }

                    Response.Headers["ETag"] = filteredHash;
                    return Ok(filteredNotifications);
                }
            }

            // Fallback (si pas d'user, on renvoie tout ou rien, ici tout pour compatibilité)
            // Dans un système ultra-strict, on renverrait une liste vide ici.
            var serverVersion = NotificationManager.Instance.GetVersionHash();
            if (Request.Headers.TryGetValue("If-None-Match", out var cTag))
            {
                if (cTag.ToString() == serverVersion) return StatusCode(304);
            }
            Response.Headers["ETag"] = serverVersion;
            return Ok(rawNotifications);
        }

        [HttpPost("BulkUserData")]
        public ActionResult GetBulkUserData([FromBody] List<string> itemIds, [FromQuery] string userId)
        {
            if (string.IsNullOrEmpty(userId) || itemIds is null || itemIds.Count == 0 || !Guid.TryParse(userId, out var userGuid))
            {
                return Ok(new Dictionary<string, bool>());
            }

            var user = _userManager.GetUserById(userGuid); 
            if (user == null) return Ok(new Dictionary<string, bool>());

            var result = new Dictionary<string, bool>(itemIds.Count);

            foreach (var idStr in itemIds)
            {
                result[idStr] = false;

                if (Guid.TryParse(idStr, out var guid))
                {
                    var item = _libraryManager.GetItemById(guid);
                    // On vérifie aussi l'accès ici par sécurité
                    if (item is not null && item.IsVisible(user))
                    {
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

            var data = GetCachedUserData();
            if (data.TryGetValue(userId, out var date)) return Ok(date);
            return Ok("2000-01-01T00:00:00.000Z");
        }

        [HttpPost("LastSeen/{userId}")]
        public ActionResult SetLastSeen(string userId, [FromQuery] string date)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(date)) return BadRequest();
            
            var data = GetCachedUserData();
            data.AddOrUpdate(userId, date, (_, _) => date);
            
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
            if (_userDataCache == null)
            {
                lock (_fileLock)
                {
                    if (_userDataCache == null)
                    {
                        var path = Path.Combine(Plugin.Instance!.DataFolderPath, "user_data.json");
                        if (!System.IO.File.Exists(path)) 
                        {
                            _userDataCache = new ConcurrentDictionary<string, string>();
                        }
                        else
                        {
                            try 
                            {
                                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                                var dict = JsonSerializer.Deserialize(fs, ControllerJsonContext.Default.DictionaryStringString);
                                _userDataCache = new ConcurrentDictionary<string, string>(dict ?? []);
                            }
                            catch { _userDataCache = new ConcurrentDictionary<string, string>(); }
                        }
                    }
                }
            }
            return _userDataCache;
        }

        private void SaveUserDataToDisk(ConcurrentDictionary<string, string> data)
        {
            try {
                var path = Path.Combine(Plugin.Instance!.DataFolderPath, "user_data.json");
                var dictToSave = new Dictionary<string, string>(data);
                
                using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                JsonSerializer.Serialize(fs, dictToSave, ControllerJsonContext.Default.DictionaryStringString);
            } catch { }
        }

        [HttpGet("Client.js")]
        [Produces("application/javascript")]
        public ActionResult GetScript()
        {
            var assembly = typeof(NotifyController).Assembly;
            var stream = assembly.GetManifestResourceStream("NotifySync.client.js");
            if (stream == null) return NotFound();
            return File(stream, "application/javascript");
        }
    }

    [JsonSerializable(typeof(Dictionary<string, string>))]
    [JsonSerializable(typeof(Dictionary<string, bool>))]
    internal partial class ControllerJsonContext : JsonSerializerContext { }
}