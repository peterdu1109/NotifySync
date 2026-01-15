using System;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Library;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;
using System.Security.Claims;
using MediaBrowser.Controller.Entities;
using Microsoft.AspNetCore.Http; // Ajouté par sécurité, mais on utilise l'indexeur ci-dessous

namespace NotifySync
{
    [ApiController]
    [Route("NotifySync")]
    public class NotifyController : ControllerBase
    {
        private readonly IUserManager _userManager;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserDataManager _userDataManager;
        private static readonly object _fileLock = new object();

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
            if (NotificationManager.Instance == null) return Ok(new List<object>());

            // 1. Récupération légère du hash
            var serverVersion = NotificationManager.Instance.GetVersionHash();
            
            // 2. Vérification ETag
            if (Request.Headers.TryGetValue("If-None-Match", out var clientTag))
            {
                if (clientTag.ToString() == serverVersion) 
                {
                    return StatusCode(304); // Not Modified
                }
            }

            // 3. Ajout du nouvel ETag (CORRECTION ICI : Utilisation de l'indexeur)
            Response.Headers["ETag"] = serverVersion;
            
            return Ok(NotificationManager.Instance.GetRecentNotifications());
        }

        [HttpPost("BulkUserData")]
        public ActionResult GetBulkUserData([FromBody] List<string> itemIds, [FromQuery] string userId)
        {
            if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var userGuid))
            {
                return Ok(new Dictionary<string, bool>());
            }

            var user = _userManager.GetUserById(userGuid); 
            if (user == null || itemIds == null || itemIds.Count == 0) 
                return Ok(new Dictionary<string, bool>());

            var result = new Dictionary<string, bool>(itemIds.Count);

            foreach (var idStr in itemIds)
            {
                if (Guid.TryParse(idStr, out var guid))
                {
                    var item = _libraryManager.GetItemById(guid);
                    
                    if (item != null)
                    {
                        var userData = _userDataManager.GetUserData(user, item);
                        bool isPlayed = false;
                        if (userData != null)
                        {
                            if (userData.Played) 
                            {
                                isPlayed = true;
                            }
                            else if (item.RunTimeTicks.HasValue && item.RunTimeTicks > 0)
                            {
                                double position = userData.PlaybackPositionTicks;
                                double duration = item.RunTimeTicks.Value;
                                if ((position / duration) * 100 > 90) isPlayed = true;
                            }
                        }
                        result[idStr] = isPlayed;
                    }
                    else
                    {
                        result[idStr] = false; 
                    }
                }
            }
            return Ok(result);
        }

        [HttpGet("LastSeen/{userId}")]
        public ActionResult GetLastSeen(string userId)
        {
            var data = LoadUserData();
            if (data.TryGetValue(userId, out var date)) return Ok(date);
            return Ok("2000-01-01T00:00:00.000Z");
        }

        [HttpPost("LastSeen/{userId}")]
        public ActionResult SetLastSeen(string userId, [FromQuery] string date)
        {
            if (string.IsNullOrEmpty(date)) return BadRequest();
            lock (_fileLock)
            {
                var data = LoadUserData();
                data[userId] = date;
                SaveUserData(data);
            }
            return Ok();
        }

        private Dictionary<string, string> LoadUserData()
        {
            try {
                var path = Path.Combine(Plugin.Instance!.DataFolderPath, "user_data.json");
                if (!System.IO.File.Exists(path)) return new Dictionary<string, string>();
                return JsonSerializer.Deserialize<Dictionary<string, string>>(System.IO.File.ReadAllText(path)) ?? new Dictionary<string, string>();
            } catch { return new Dictionary<string, string>(); }
        }

        private void SaveUserData(Dictionary<string, string> data)
        {
            try {
                var path = Path.Combine(Plugin.Instance!.DataFolderPath, "user_data.json");
                System.IO.File.WriteAllText(path, JsonSerializer.Serialize(data));
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
}