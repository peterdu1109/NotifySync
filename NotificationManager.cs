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

        public void Refresh()
        {
            lock (_lock)
            {
                _notifications.Clear();
            }
            PopulateInitialHistory();
            SaveNotifications();
        }

        private void PopulateInitialHistory()
        {
            try
            {
                // SCAN LARGE (300 items) pour trouver de la diversité
                var query = new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Episode, BaseItemKind.MusicAlbum },
                    OrderBy = new[] { (Jellyfin.Data.Enums.ItemSortBy.DateCreated, Jellyfin.Database.Implementations.Enums.SortOrder.Descending) },
                    Limit = 300, 
                    Recursive = true,
                    IsVirtualItem = false
                };

                var items = _libraryManager.GetItemList(query);
                var tempNotifs = new List<NotificationItem>();
                
                foreach (var item in items)
                {
                    var notif = CreateNotificationFromItem(item);
                    if (notif != null) tempNotifs.Add(notif);
                }

                // Application des quotas par catégorie
                ApplyCategoryQuotas(tempNotifs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur scan historique.");
            }
        }

        private void ApplyCategoryQuotas(List<NotificationItem> newItems)
        {
            var config = Plugin.Instance?.Configuration;
            int limitPerCat = config?.MaxItems ?? 5; 
            if (limitPerCat < 3) limitPerCat = 3;

            lock (_lock)
            {
                _notifications.AddRange(newItems);

                // --- ALGORITHME DE QUOTAS ---
                // Garde les X meilleurs de chaque catégorie
                _notifications = _notifications
                    .GroupBy(n => n.Category)
                    .SelectMany(g => g.OrderByDescending(x => x.DateCreated).Take(limitPerCat))
                    .OrderByDescending(x => x.DateCreated)
                    .ToList();
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

        private void OnItemAdded(object? sender, ItemChangeEventArgs e)
        {
            var notif = CreateNotificationFromItem(e.Item);
            if (notif != null)
            {
                ApplyCategoryQuotas(new List<NotificationItem> { notif });
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

            var config = Plugin.Instance?.Configuration;
            if (config == null) return null;

            // --- DETECTION ROBUSTE (Nom ou ID) ---
            var allParents = item.GetParents().ToList();
            var rootLibrary = _libraryManager.GetCollectionFolders(item).FirstOrDefault();
            if (rootLibrary != null && !allParents.Contains(rootLibrary)) allParents.Add(rootLibrary);

            var enabledIds = config.EnabledLibraries ?? new List<string>();
            var manualEntries = config.ManualLibraryIds ?? new List<string>();
            manualEntries = manualEntries.Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();

            string? matchedLibraryId = null;
            bool isAllowed = false;

            if (enabledIds.Count == 0 && manualEntries.Count == 0)
            {
                isAllowed = true;
                if (rootLibrary != null) matchedLibraryId = rootLibrary.Id.ToString();
            }
            else
            {
                foreach (var parent in allParents)
                {
                    string pId = parent.Id.ToString();
                    string pIdSimple = parent.Id.ToString("N");
                    string pName = parent.Name;

                    if (enabledIds.Contains(pId) || enabledIds.Contains(pIdSimple)) { isAllowed = true; matchedLibraryId = pId; break; }
                    if (manualEntries.Any(m => m.Equals(pId, StringComparison.OrdinalIgnoreCase) || m.Equals(pIdSimple, StringComparison.OrdinalIgnoreCase))) { isAllowed = true; matchedLibraryId = pId; break; }
                    if (manualEntries.Any(m => m.Equals(pName, StringComparison.OrdinalIgnoreCase))) { isAllowed = true; matchedLibraryId = pId; break; }
                }
            }

            if (!isAllowed) return null;

            // --- CATEGORIES ---
            string category = "Movie";
            if (isEpisode) category = "Series";
            else if (isMusic) category = "Music";

            if (matchedLibraryId != null && config.CategoryMappings != null)
            {
                var mapping = config.CategoryMappings.FirstOrDefault(m => 
                    string.Equals(m.LibraryId, matchedLibraryId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(m.LibraryId, matchedLibraryId.Replace("-", ""), StringComparison.OrdinalIgnoreCase));
                
                if (mapping != null && !string.IsNullOrEmpty(mapping.CategoryName))
                {
                    category = mapping.CategoryName;
                }
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
                Overview = item.Overview,
                BackdropImageTags = backdropTags
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
            lock (_lock) { return _notifications.ToList(); }
        }

        public void Dispose()
        {
            if (_libraryManager != null)
            {
                _libraryManager.ItemAdded -= OnItemAdded;
                _libraryManager.ItemRemoved -= OnItemRemoved;
            }
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
        public string? Overview { get; set; }
        public List<string> BackdropImageTags { get; set; } = new List<string>();
    }
}