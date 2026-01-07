using System;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace NotifySync
{
    [ApiController]
    [Route("NotifySync")]
    public class NotifyController : ControllerBase
    {
        private static readonly object _configLock = new object();
        private readonly IUserManager _userManager;
        private readonly ILibraryManager _libraryManager;
        private readonly IFileSystem _fileSystem;
        private readonly ILogger<NotificationManager> _logger; // Inject Generic logger for Manager

        public NotifyController(IUserManager userManager, ILibraryManager libraryManager, IFileSystem fileSystem, ILogger<NotificationManager> logger)
        {
            _userManager = userManager;
            _libraryManager = libraryManager;
            _fileSystem = fileSystem;
            _logger = logger;
        }

        [HttpGet("Config")]
        public ActionResult GetConfig()
        {
            if (Plugin.Instance == null) return NotFound();
            return Ok(Plugin.Instance.Configuration);
        }

        [HttpGet("Data")]
        [ResponseCache(Duration = 60)] // Cache for 60 seconds
        public ActionResult GetData()
        {
            // LAZY INIT
            if (NotificationManager.Instance == null)
            {
                new NotificationManager(_libraryManager, _logger, _fileSystem);
                // Optional: Force first scan if empty? 
                // NotificationManager loads json. If json empty (first run), we might want to scan?
                // For now, rely on events + JSON.
            }
            
            if (NotificationManager.Instance == null) return Ok(new List<object>());
            return Ok(NotificationManager.Instance.GetRecentNotifications());
        }

        [HttpGet("Audio/{itemId}")]
        public ActionResult GetAudio(string itemId)
        {
             // Dedicated Audio Endpoint
            // Logic: Find item, get theme song, stream it.
            // Simplified for now: Client still queries API or we implement full stream logic later if required.
            // For now, let's stick to returning Data.
            return NotFound("Audio proxy not fully implemented yet");
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
            // Retourne une date ancienne par défaut si rien n'est trouvé
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
                    
                    // CORRECTION DU WARNING ICI : Ajout du '?' après string
                    if (config.UserLastSeen.TryGetValue(userId, out string? currentSavedDate))
                    {
                        if (DateTime.TryParse(currentSavedDate, out DateTime saved) && 
                            DateTime.TryParse(date, out DateTime incoming))
                        {
                            // Si la date entrante est plus vieille que celle sauvegardée, on ignore
                            if (incoming <= saved) shouldUpdate = false;
                        }
                    }

                    if (shouldUpdate)
                    {
                        config.UserLastSeen[userId] = date;
                        
                        // Nettoyage opportuniste (1 chance sur 10) des utilisateurs supprimés
                        if (new Random().Next(0, 10) == 0) 
                        {
                            CleanupUsers(config);
                        }

                        Plugin.Instance.UpdateConfiguration(config);
                    }
                }
            }
            return Ok();
        }

        private void CleanupUsers(Configuration.PluginConfiguration config)
        {
            try 
            {
                var validUserIds = _userManager.Users.Select(u => u.Id.ToString("N")).ToHashSet();
                // Nettoyage des IDs qui ne sont plus dans Jellyfin
                var keysToRemove = config.UserLastSeen.Keys
                    .Where(k => !validUserIds.Contains(k.Replace("-", "")))
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    config.UserLastSeen.Remove(key);
                }
            }
            catch { /* Ignorer pour éviter un crash serveur */ }
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