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

        public NotifyController(IUserManager userManager, ILibraryManager libraryManager, IUserDataManager userDataManager)
        {
            _userManager = userManager;
            _libraryManager = libraryManager;
            _userDataManager = userDataManager;
        }

        [HttpPost("Refresh")]
        public ActionResult Refresh()
        {
            if (NotificationManager.Instance != null)
            {
                NotificationManager.Instance.Refresh();
                return Ok("Refreshed");
            }
            return NotFound();
        }

        [HttpGet("Data")]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)] 
        public ActionResult GetData()
        {
            if (NotificationManager.Instance == null) return Ok(Array.Empty<object>());

            var serverVersion = NotificationManager.Instance.GetVersionHash();
            
            if (Request.Headers.TryGetValue("If-None-Match", out var clientTag))
            {
                if (clientTag.ToString() == serverVersion) return StatusCode(304);
            }

            Response.Headers["ETag"] = serverVersion;
            return Ok(NotificationManager.Instance.GetRecentNotifications());
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
                    if (item is not null)
                    {
                        var userData = _userDataManager.GetUserData(user, item);
                        if (userData is not null)
                        {
                            if (userData.Played) 
                            {
                                result[idStr] = true;
                            }
                            // OPTIMISATION : Remplacement de la division (lente) par une multiplication
                            // Vérifie si lu à plus de 90% (0.9)
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
            var data = GetCachedUserData();
            if (data.TryGetValue(userId, out var date)) return Ok(date);
            return Ok("2000-01-01T00:00:00.000Z");
        }

        [HttpPost("LastSeen/{userId}")]
        public ActionResult SetLastSeen(string userId, [FromQuery] string date)
        {
            if (string.IsNullOrEmpty(date)) return BadRequest();
            
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