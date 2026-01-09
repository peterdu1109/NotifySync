using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Jellyfin.Data.Enums; 

namespace NotifySync
{
    public class NotificationManager : IDisposable
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger<NotificationManager> _logger;
        private readonly IFileSystem _fileSystem;
        private readonly string _jsonPath;
        private readonly Timer _cleanupTimer;
        
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

            if (_notifications.Count == 0)
            {
                PopulateInitialHistory();
            }

            _libraryManager.ItemAdded += OnItemAdded;
            _libraryManager.ItemRemoved += OnItemRemoved;
        }

        private void PopulateInitialHistory()
        {
            try
            {
                var query = new InternalItemsQuery
                {
                    // 1. Les types d'items (Jellyfin.Data)
                    IncludeItemTypes = new[] { 
                        BaseItemKind.Movie, 
                        BaseItemKind.Episode, 
                        BaseItemKind.MusicAlbum 
                    },
                    
                    // 2. L'ordre de tri (CORRIGÉ GRÂCE AU MESSAGE D'ERREUR)
                    // On utilise les types exacts demandés par le compilateur
                    OrderBy = new[] { 
                        (Jellyfin.Data.Enums.ItemSortBy.DateCreated, Jellyfin.Database.Implementations.Enums.SortOrder.Descending) 
                    },
                    
                    Limit = 10,
                    Recursive = true,
                    IsVirtualItem = false
                };

                var items = _libraryManager.GetItemList(query);

                foreach (var item in items)
                {
                    var notif = CreateNotificationFromItem(item);
                    if (notif != null)
                    {
                        _notifications.Add(notif);
                    }
                }
                
                if (_notifications.Count > 0) SaveNotifications();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du scan initial de l'historique.");
            }
        }

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
            var notif = CreateNotificationFromItem(e.Item);
            if (notif != null)
            {
                lock (_lock)
                {
                    _notifications.Insert(0, notif);
                    if (_notifications.Count > 50) _notifications = _notifications.Take(50).ToList();
                }
                SaveNotifications();
            }
        }

        private NotificationItem? CreateNotificationFromItem(BaseItem item)
        {
            if (item is Folder || item.IsVirtualItem) return null;

            bool isMovie = item is MediaBrowser.Controller.Entities.Movies.Movie;
            bool isEpisode = item is MediaBrowser.Controller.Entities.TV.Episode;
            bool isMusic = item is MediaBrowser.Controller.Entities.Audio.MusicAlbum;

            if (!isMovie && !isEpisode && !isMusic) return null;

            var config = Plugin.Instance!.Configuration;
            var allowedLibraryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            if (config.EnabledLibraries != null) foreach (var id in config.EnabledLibraries) allowedLibraryIds.Add(id);
            if (config.ManualLibraryIds != null) foreach(var id in config.ManualLibraryIds) allowedLibraryIds.Add(id);

            string? matchedLibraryId = null;

            if (allowedLibraryIds.Count > 0)
            {
                bool isAllowed = false;
                var current = item;
                while (current != null)
                {
                    if (allowedLibraryIds.Contains(current.Id.ToString()) || allowedLibraryIds.Contains(current.Id.ToString("N")))
                    {
                        isAllowed = true;
                        matchedLibraryId = current.Id.ToString();
                        break;
                    }
                    current = current.GetParent();
                    if (current is AggregateFolder) break;
                }
                if (!isAllowed) return null;
            }

            string category = "Movie";
            if (isEpisode) category = "Series";
            else if (isMusic) category = "Music";

            if (matchedLibraryId == null)
            {
                 var current = item;
                 while(current != null)
                 {
                     var parent = current.GetParent();
                     if (parent is AggregateFolder || parent == null) 
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
            }

            var backdropTags = new List<string>();
            var imgInfo = item.GetImageInfo(ImageType.Backdrop, 0);
            if (imgInfo != null)
            {
                backdropTags.Add(imgInfo.DateModified.Ticks.ToString("x"));
            }

            return new NotificationItem
            {
                Id = item.Id.ToString(),
                Name = item.Name,
                Category = category,
                SeriesName = (item as MediaBrowser.Controller.Entities.TV.Episode)?.SeriesName,
                SeriesId = (item as MediaBrowser.Controller.Entities.TV.Episode)?.SeriesId.ToString(),
                DateCreated = item.DateCreated,
                Type = item.GetType().Name,
                RunTimeTicks = item.RunTimeTicks,
                ProductionYear = item.ProductionYear,
                CommunityRating = item.CommunityRating,
                Overview = item.Overview,
                Genres = item.Genres.ToList(),
                BackdropImageTags = backdropTags,
                ParentIndexNumber = (item as MediaBrowser.Controller.Entities.TV.Episode)?.ParentIndexNumber,
                IndexNumber = (item as MediaBrowser.Controller.Entities.TV.Episode)?.IndexNumber
            };
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
                return _notifications.ToList();
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
        public string Category { get; set; } = string.Empty;
        public string? SeriesName { get; set; }
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