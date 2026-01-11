using System;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Library;
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
        [ResponseCache(Duration = 60)]
        public ActionResult GetData()
        {
            if (NotificationManager.Instance == null) return Ok(new List<object>());
            return Ok(NotificationManager.Instance.GetRecentNotifications());
        }

        [HttpGet("LastSeen/{userId}")]
        public ActionResult GetLastSeen(string userId)
        {
            if (Plugin.Instance == null) return Ok("2000-01-01T00:00:00.000Z");
            lock (_configLock)
            {
                if (Plugin.Instance.Configuration.UserLastSeen.TryGetValue(userId, out var lastSeenDate))
                    return Ok(lastSeenDate);
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
                    config.UserLastSeen[userId] = date;
                    Plugin.Instance.UpdateConfiguration(config);
                    
                    if (new Random().Next(0, 20) == 0) Task.Run(() => CleanupUsers(config));
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
            catch { }
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