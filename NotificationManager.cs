using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;

namespace NotifySync
{
    public class NotificationManager : IDisposable
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger<NotificationManager> _logger;
        private readonly IFileSystem _fileSystem;
        private readonly string _jsonPath;
        private readonly Timer _cleanupTimer;
        
        // In-Memory cache of notifications
        private List<NotificationItem> _notifications = new List<NotificationItem>();
        private readonly object _lock = new object();

        public static NotificationManager? Instance { get; private set; }

        public NotificationManager(ILibraryManager libraryManager, ILogger<NotificationManager> logger, IFileSystem fileSystem)
        {
            _libraryManager = libraryManager;
            _logger = logger;
            _fileSystem = fileSystem;
            _jsonPath = Path.Combine(Plugin.Instance!.DataFolderPath, "notifications.json");
            Instance = this;
            
            _cleanupTimer = new Timer(CleanupRoutine, null, TimeSpan.FromMinutes(10), TimeSpan.FromHours(1));
            LoadNotifications();
            _libraryManager.ItemAdded += OnItemAdded;
            _libraryManager.ItemRemoved += OnItemRemoved;
        }

        // RunAsync removed.

        private void LoadNotifications()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(_jsonPath))
                    {
                        var json = File.ReadAllText(_jsonPath);
                        _notifications = JsonSerializer.Deserialize<List<NotificationItem>>(json) ?? new List<NotificationItem>();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading notifications.json");
                    _notifications = new List<NotificationItem>();
                }
            }
        }

        private void SaveNotifications()
        {
            lock (_lock)
            {
                try
                {
                    var json = JsonSerializer.Serialize(_notifications);
                    File.WriteAllText(_jsonPath, json);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error saving notifications.json");
                }
            }
        }

        private void OnItemAdded(object? sender, ItemChangeEventArgs e)
        {
            var item = e.Item;
            if (item is Folder || item.IsVirtualItem) return;

            // Simple Filter: Movies and Episodes
            if (!(item is MediaBrowser.Controller.Entities.Movies.Movie) && !(item is MediaBrowser.Controller.Entities.TV.Episode)) return;

            // Check Library Config (Admin selection)
            var config = Plugin.Instance!.Configuration;
            
            // Determine active lists (Checkbox + Manual)
            var enabledIds = new HashSet<string>(config.EnabledLibraries ?? new List<string>());
            if (config.ManualLibraryIds != null) 
            {
                foreach(var id in config.ManualLibraryIds) enabledIds.Add(id);
            }

            string? matchedLibraryId = null;

            if (enabledIds.Any())
            {
                // Recursive check for Library Root and capture LibraryId
                bool isAllowed = false;
                var current = item;
                while (current != null)
                {
                    var cid = current.Id.ToString();
                    // Check strict and guid format
                    if (enabledIds.Contains(cid) || enabledIds.Contains(current.Id.ToString("N")))
                    {
                        isAllowed = true;
                        matchedLibraryId = cid; // Capture for category mapping
                        break;
                    }
                    current = current.GetParent();
                    if (current is MediaBrowser.Controller.Entities.AggregateFolder) break;
                }
                
                if (!isAllowed) return; // Not in enabled libraries
            }

            // Determine Category
            string category = item is MediaBrowser.Controller.Entities.TV.Episode ? "Series" : "Movie"; // Default
            if (matchedLibraryId == null)
            {
                // If we didn't filter (enabledIds empty), we still need library ID for mapping
                // Try to find root manually
                 var current = item;
                 while(current != null)
                 {
                     var parent = current.GetParent();
                     if (parent is MediaBrowser.Controller.Entities.AggregateFolder || parent == null) 
                     {
                         matchedLibraryId = current.Id.ToString();
                         break;
                     }
                     current = parent;
                 }
            }

            if (matchedLibraryId != null && config.CategoryMappings != null)
            {
                var mapping = config.CategoryMappings.FirstOrDefault(m => m.LibraryId == matchedLibraryId);
                if (mapping != null && !string.IsNullOrEmpty(mapping.CategoryName))
                {
                    category = mapping.CategoryName;
                }
            } else if (config.CategoryMappings != null)
            {
                 // Fallback check: maybe we have strict GUID format mismatch?
                 // Iterating mappings is safer if we missed the ID above
            }

            var notif = new NotificationItem
            {
                Id = item.Id.ToString(),
                Name = item.Name,
                Category = category,
                SeriesName = (item as MediaBrowser.Controller.Entities.TV.Episode)?.SeriesName,
                SeriesId = (item as MediaBrowser.Controller.Entities.TV.Episode)?.SeriesId.ToString(),
                DateCreated = item.DateCreated,
                Type = item.GetType().Name, // "Movie" or "Episode"
                RunTimeTicks = item.RunTimeTicks,
                ProductionYear = item.ProductionYear,
                CommunityRating = item.CommunityRating,
                Overview = item.Overview,
                Genres = item.Genres.ToList(),
                BackdropImageTags = new List<string>(), // Disabled due to API change, client will fallback
                ParentIndexNumber = (item as MediaBrowser.Controller.Entities.TV.Episode)?.ParentIndexNumber,
                IndexNumber = (item as MediaBrowser.Controller.Entities.TV.Episode)?.IndexNumber
            };

            lock (_lock)
            {
                _notifications.Insert(0, notif);
                // Keep max 50 items
                if (_notifications.Count > 50) _notifications = _notifications.Take(50).ToList();
            }
            SaveNotifications();
        }

        private void OnItemRemoved(object? sender, ItemChangeEventArgs e)
        {
            lock (_lock)
            {
                int removed = _notifications.RemoveAll(x => x.Id == e.Item.Id.ToString());
                if (removed > 0) SaveNotifications();
            }
        }

        private void CleanupRoutine(object? state)
        {
            // Auto cleanup items older than 30 days
            lock (_lock)
            {
                var cutoff = DateTime.UtcNow.AddDays(-30);
                int removed = _notifications.RemoveAll(x => x.DateCreated < cutoff);
                if (removed > 0) SaveNotifications();
            }
        }

        public List<NotificationItem> GetRecentNotifications()
        {
            lock (_lock)
            {
                return _notifications.ToList(); // Return copy
            }
        }

        public void Dispose()
        {
            _libraryManager.ItemAdded -= OnItemAdded;
            _libraryManager.ItemRemoved -= OnItemRemoved;
            _cleanupTimer?.Dispose();
        }
    }

    public class NotificationItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty; // New field
        public string? SeriesName { get; set; } // Null for movies
        public string? SeriesId { get; set; }
        public DateTime DateCreated { get; set; }
        public string Type { get; set; } = string.Empty;
        public long? RunTimeTicks { get; set; }
        public int? ProductionYear { get; set; }
        public float? CommunityRating { get; set; }
        public string? Overview { get; set; }
        public List<string> Genres { get; set; } = new List<string>();
        public List<string> BackdropImageTags { get; set; } = new List<string>();
        public int? ParentIndexNumber { get; set; }
        public int? IndexNumber { get; set; }
    }
}
