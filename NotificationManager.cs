using System;
using System.Collections.Concurrent;
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
        private readonly string _jsonPath;
        
        private readonly ConcurrentQueue<BaseItem> _eventBuffer = new ConcurrentQueue<BaseItem>();
        private Timer? _bufferProcessTimer;
        private readonly object _lock = new object();
        private List<NotificationItem> _notifications = new List<NotificationItem>();

        public static NotificationManager? Instance { get; private set; }

        public NotificationManager(ILibraryManager libraryManager, ILogger<NotificationManager> logger, IFileSystem fileSystem)
        {
            _libraryManager = libraryManager;
            _logger = logger;
            _jsonPath = Path.Combine(Plugin.Instance!.DataFolderPath, "notifications.json");
            Instance = this;
            
            LoadNotifications();
            _bufferProcessTimer = new Timer(ProcessBuffer, null, 2000, 2000);

            if (_notifications.Count == 0)
            {
                System.Threading.Tasks.Task.Run(PopulateInitialHistory);
            }

            _libraryManager.ItemAdded += OnItemAdded;
            _libraryManager.ItemRemoved += OnItemRemoved;
        }

        public void Refresh()
        {
            lock (_lock) { _notifications.Clear(); }
            PopulateInitialHistory();
            SaveNotifications();
        }

        private void PopulateInitialHistory()
        {
            try
            {
                var tempNotifs = new List<NotificationItem>();
                var typesToScan = new[] { BaseItemKind.Movie, BaseItemKind.Episode, BaseItemKind.MusicAlbum };

                foreach (var type in typesToScan)
                {
                    var query = new InternalItemsQuery
                    {
                        IncludeItemTypes = new[] { type },
                        OrderBy = new[] { (Jellyfin.Data.Enums.ItemSortBy.DateCreated, Jellyfin.Database.Implementations.Enums.SortOrder.Descending) },
                        // MODIFICATION : Scan large (50) pour remplir le quota de 10 même si mixité
                        Limit = 500, 
                        Recursive = true,
                        IsVirtualItem = false
                    };
                    
                    var items = _libraryManager.GetItemList(query);
                    foreach (var item in items)
                    {
                        var notif = CreateNotificationFromItem(item);
                        if (notif != null) tempNotifs.Add(notif);
                    }
                }

                ApplyCategoryQuotas(tempNotifs, isInitialLoad: true);
                SaveNotifications();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur scan historique.");
            }
        }

        private void OnItemAdded(object? sender, ItemChangeEventArgs e)
        {
            if(e.Item != null && !e.Item.IsVirtualItem) _eventBuffer.Enqueue(e.Item);
        }

        private void ProcessBuffer(object? state)
        {
            if (_eventBuffer.IsEmpty) return;

            var newItems = new List<NotificationItem>();
            while (_eventBuffer.TryDequeue(out var item))
            {
                try 
                {
                    var notif = CreateNotificationFromItem(item);
                    if (notif != null) newItems.Add(notif);
                }
                catch { }
            }

            if (newItems.Any())
            {
                ApplyCategoryQuotas(newItems);
                SaveNotifications();
            }
        }

        private void ApplyCategoryQuotas(List<NotificationItem> incoming, bool isInitialLoad = false)
        {
            var config = Plugin.Instance?.Configuration;
            // On s'assure que le minimum est 3, sinon valeur config (défaut 10)
            int limitPerCat = config?.MaxItems ?? 10; 
            if (limitPerCat < 3) limitPerCat = 3;

            lock (_lock)
            {
                if (isInitialLoad) _notifications.Clear();
                _notifications.AddRange(incoming);

                _notifications = _notifications
                    .GroupBy(n => n.Category)
                    .SelectMany(g => g.OrderByDescending(x => x.DateCreated).Take(limitPerCat))
                    .OrderByDescending(x => x.DateCreated)
                    .ToList();
            }
        }

        private NotificationItem? CreateNotificationFromItem(BaseItem item)
        {
            if (item is Folder || item.IsVirtualItem) return null;

            bool isMovie = item is MediaBrowser.Controller.Entities.Movies.Movie;
            bool isEpisode = item is MediaBrowser.Controller.Entities.TV.Episode;
            bool isMusic = item is MediaBrowser.Controller.Entities.Audio.MusicAlbum;

            if (!isMovie && !isEpisode && !isMusic) return null;

            var config = Plugin.Instance?.Configuration;
            if (config == null) return null;

            // --- FILTRAGE ROBUSTE ---
            bool hasRestrictions = (config.EnabledLibraries?.Any() == true) || (config.ManualLibraryIds?.Any() == true);
            string? matchedLibraryId = null;

            var allParents = item.GetParents().ToList();
            var rootLibrary = _libraryManager.GetCollectionFolders(item).FirstOrDefault();
            if (rootLibrary != null && !allParents.Contains(rootLibrary)) allParents.Add(rootLibrary);

            if (hasRestrictions)
            {
                var enabledIds = config.EnabledLibraries ?? new List<string>();
                var manualEntries = config.ManualLibraryIds ?? new List<string>();

                foreach (var parent in allParents)
                {
                    string pId = parent.Id.ToString();
                    string pIdSimple = parent.Id.ToString("N");
                    
                    if (enabledIds.Any(e => e.Equals(pId, StringComparison.OrdinalIgnoreCase) || e.Equals(pIdSimple, StringComparison.OrdinalIgnoreCase))) 
                    { matchedLibraryId = pId; break; }
                    
                    if (manualEntries.Any(m => m.Equals(pId, StringComparison.OrdinalIgnoreCase) || m.Equals(pIdSimple, StringComparison.OrdinalIgnoreCase) || m.Equals(parent.Name, StringComparison.OrdinalIgnoreCase))) 
                    { matchedLibraryId = pId; break; }
                }
                
                if (matchedLibraryId == null) return null;
            }
            else if (rootLibrary != null)
            {
                matchedLibraryId = rootLibrary.Id.ToString();
            }

            // --- MAPPING CATEGORIES (Normalisé) ---
            string category = "Movie";
            if (isEpisode) category = "Series";
            else if (isMusic) category = "Music";

            if (matchedLibraryId != null && config.CategoryMappings != null)
            {
                string normalizedMatched = matchedLibraryId.Replace("-", "");
                var mapping = config.CategoryMappings.FirstOrDefault(m => 
                    m.LibraryId.Replace("-", "").Equals(normalizedMatched, StringComparison.OrdinalIgnoreCase));
                
                if (mapping != null && !string.IsNullOrEmpty(mapping.CategoryName)) category = mapping.CategoryName;
            }

            var backdropTags = new List<string>();
            var imgInfo = item.GetImageInfo(ImageType.Backdrop, 0);
            if (imgInfo != null) backdropTags.Add(imgInfo.DateModified.Ticks.ToString("x"));

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
                BackdropImageTags = backdropTags,
                PrimaryImageTag = item.GetImageInfo(ImageType.Primary, 0)?.DateModified.Ticks.ToString("x")
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
                catch { _notifications = new List<NotificationItem>(); }
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
                catch (Exception ex) { _logger.LogError(ex, "Erreur sauvegarde notifications."); }
            }
        }

        public List<NotificationItem> GetRecentNotifications()
        {
            lock (_lock) { return _notifications.ToList(); }
        }

        public void Dispose()
        {
            _bufferProcessTimer?.Dispose();
            if (_libraryManager != null)
            {
                _libraryManager.ItemAdded -= OnItemAdded;
                _libraryManager.ItemRemoved -= OnItemRemoved;
            }
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
        public List<string> BackdropImageTags { get; set; } = new List<string>();
        public string? PrimaryImageTag { get; set; }
    }
}