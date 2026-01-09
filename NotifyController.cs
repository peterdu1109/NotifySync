using System;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace NotifySync
{
    [ApiController]
    [Route("NotifySync")]
    public class NotifyController : ControllerBase
    {
        private static readonly object _configLock = new object();
        private readonly IUserManager _userManager;
        
        public NotifyController(IUserManager userManager)
        {
            _userManager = userManager;
        }

        [HttpGet("Config")]
        public ActionResult GetConfig()
        {
            if (Plugin.Instance == null) return NotFound();
            return Ok(Plugin.Instance.Configuration);
        }

        [HttpGet("Data")]
        [ResponseCache(Duration = 60)]
        public ActionResult GetData()
        {
            if (NotificationManager.Instance == null) return Ok(new List<object>());
            return Ok(NotificationManager.Instance.GetRecentNotifications());
        }

        [HttpGet("LastSeen/{userId}")]
        public ActionResult GetLastSeen(string userId)
        {
            if (Plugin.Instance == null) return Ok("");
            
            lock (_configLock)
            {
                if (Plugin.Instance.Configuration.UserLastSeen.TryGetValue(userId, out var lastSeenDate))
                {
                    return Ok(lastSeenDate);
                }
            }
            return Ok("2000-01-01T00:00:00.000Z");
        }

        [HttpPost("LastSeen/{userId}")]
        public ActionResult SetLastSeen(string userId, [FromQuery] string date)
        {
            if (Plugin.Instance != null && !string.IsNullOrEmpty(date))
            {
                lock (_configLock)
                {
                    var config = Plugin.Instance.Configuration;
                    bool shouldUpdate = true;
                    
                    if (config.UserLastSeen.TryGetValue(userId, out string? currentSavedDate))
                    {
                        if (DateTime.TryParse(currentSavedDate, out DateTime saved) && 
                            DateTime.TryParse(date, out DateTime incoming))
                        {
                            if (incoming <= saved) shouldUpdate = false;
                        }
                    }

                    if (shouldUpdate)
                    {
                        config.UserLastSeen[userId] = date;
                        Plugin.Instance.UpdateConfiguration(config);

                        // Nettoyage en tâche de fond (Fire and Forget) pour ne pas ralentir la réponse
                        if (new Random().Next(0, 10) == 0) 
                        {
                            Task.Run(() => CleanupUsers(config));
                        }
                    }
                }
            }
            return Ok();
        }

        private void CleanupUsers(Configuration.PluginConfiguration config)
        {
            try 
            {
                lock(_configLock) 
                {
                    var validUserIds = _userManager.Users.Select(u => u.Id.ToString("N")).ToHashSet();
                    var keysToRemove = config.UserLastSeen.Keys
                        .Where(k => !validUserIds.Contains(k.Replace("-", "")))
                        .ToList();

                    if (keysToRemove.Any())
                    {
                        foreach (var key in keysToRemove) config.UserLastSeen.Remove(key);
                        Plugin.Instance?.UpdateConfiguration(config);
                    }
                }
            }
            catch { /* Sécurité anti-crash */ }
        }

        [HttpGet("Client.js")]
        [Produces("application/javascript")]
        public ActionResult GetScript()
        {
            var assembly = typeof(NotifyController).Assembly;
            var resourceName = "NotifySync.client.js";
            var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return NotFound();
            return File(stream, "application/javascript");
        }
    }
}