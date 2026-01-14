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
        [ResponseCache(Duration = 10)]
        public ActionResult GetData()
        {
            if (NotificationManager.Instance == null) return Ok(new List<object>());
            return Ok(NotificationManager.Instance.GetRecentNotifications());
        }

        // --- CORRECTION APPLIQUÉE ICI ---
        // Nous acceptons maintenant l'userId explicitement via l'URL
        [HttpPost("BulkUserData")]
        public ActionResult GetBulkUserData([FromBody] List<string> itemIds, [FromQuery] string userId)
        {
            // 1. Validation de l'ID utilisateur reçu du client JS
            if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var userGuid))
            {
                // Si pas d'ID, on ne peut pas vérifier le statut "Vu" -> tout reste "Non vu"
                return Ok(new Dictionary<string, bool>());
            }

            var result = new Dictionary<string, bool>();
            var user = _userManager.GetUserById(userGuid); 

            if (user == null || itemIds == null || !itemIds.Any()) 
                return Ok(result);

            foreach (var idStr in itemIds)
            {
                if (Guid.TryParse(idStr, out var guid))
                {
                    var item = _libraryManager.GetItemById(guid);
                    if (item != null)
                    {
                        // 2. Récupération des données SPECIFIQUES à cet utilisateur
                        var userData = _userDataManager.GetUserData(user, item);
                        
                        bool isPlayed = false;
                        if (userData != null)
                        {
                            // Cas A : Marqué comme lu
                            if (userData.Played) 
                            {
                                isPlayed = true;
                            }
                            // Cas B : En cours de lecture mais presque fini (>90%)
                            else if (item.RunTimeTicks.HasValue && item.RunTimeTicks > 0)
                            {
                                double position = userData.PlaybackPositionTicks;
                                double duration = item.RunTimeTicks.Value;
                                // Calcul du pourcentage
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