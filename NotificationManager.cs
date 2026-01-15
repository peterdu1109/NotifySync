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
        
        // --- Optimisation Sauvegarde ---
        private Timer? _saveTimer;
        private bool _hasUnsavedChanges = false;
        private readonly object _saveLock = new object(); // Lock pour l'écriture disque
        private readonly object _dataLock = new object(); // Lock pour la liste _notifications
        // ------------------------------

        private List<NotificationItem> _notifications = new List<NotificationItem>();

        public static NotificationManager? Instance { get; private set; }

        public NotificationManager(ILibraryManager libraryManager, ILogger<NotificationManager> logger, IFileSystem fileSystem)
        {
            _libraryManager = libraryManager;
            _logger = logger;
            _jsonPath = Path.Combine(Plugin.Instance!.DataFolderPath, "notifications.json");
            Instance = this;
            
            LoadNotifications();
            
            // Timer 1 : Traitement des événements Jellyfin (toutes les 2s)
            _bufferProcessTimer = new Timer(ProcessBuffer, null, 2000, 2000);
            
            // Timer 2 : Sauvegarde Disque Différée (toutes les 10s)
            // Cela évite d'écrire 50 fois le fichier si on ajoute une saison entière
            _saveTimer = new Timer(SaveOnTick, null, 10000, 10000);

            if (_notifications.Count == 0)
            {
                System.Threading.Tasks.Task.Run(PopulateInitialHistory);
            }

            _libraryManager.ItemAdded += OnItemAdded;
            _libraryManager.ItemRemoved += OnItemRemoved;
        }

        public void Refresh()
        {
            // Reset complet manuel
            lock (_dataLock) { _notifications.Clear(); }
            PopulateInitialHistory();
            // Pour un refresh manuel, on force la sauvegarde immédiate
            ForceSaveNow(); 
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
                        Limit = 2000, 
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
                _hasUnsavedChanges = true;
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
                // On ne sauvegarde PLUS ici directement. On marque juste le flag.
                _hasUnsavedChanges = true; 
            }
        }

        private void ApplyCategoryQuotas(List<NotificationItem> incoming, bool isInitialLoad = false)
        {
            var config = Plugin.Instance?.Configuration;
            int limitPerCat = config?.MaxItems ?? 5; 
            if (limitPerCat < 1) limitPerCat = 5;

            lock (_dataLock)
            {
                if (isInitialLoad) _notifications.Clear();
                _notifications.AddRange(incoming);

                var filteredResults = new List<NotificationItem>();
                var categoryGroups = _notifications.GroupBy(n => n.Category);

                foreach (var catGroup in categoryGroups)
                {
                    bool containsEpisodes = catGroup.Any(x => x.Type == "Episode");

                    if (containsEpisodes)
                    {
                        var seriesClusters = catGroup
                            .GroupBy(x => x.SeriesId ?? x.Name) 
                            .Select(g => new 
                            { 
                                SeriesGroup = g, 
                                LatestDate = g.Max(x => x.DateCreated) 
                            })
                            .OrderByDescending(x => x.LatestDate)
                            .Take(limitPerCat); 

                        foreach (var cluster in seriesClusters)
                        {
                            filteredResults.AddRange(cluster.SeriesGroup);
                        }
                    }
                    else
                    {
                        filteredResults.AddRange(catGroup.OrderByDescending(x => x.DateCreated).Take(limitPerCat));
                    }
                }

                _notifications = filteredResults.OrderByDescending(x => x.DateCreated).ToList();
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

            var episode = item as MediaBrowser.Controller.Entities.TV.Episode;

            return new NotificationItem
            {
                Id = item.Id.ToString(),
                Name = item.Name,
                Category = category,
                SeriesName = episode?.SeriesName,
                SeriesId = episode?.SeriesId.ToString(),
                DateCreated = item.DateCreated,
                Type = item.GetType().Name, 
                RunTimeTicks = item.RunTimeTicks,
                ProductionYear = item.ProductionYear,
                BackdropImageTags = backdropTags,
                PrimaryImageTag = item.GetImageInfo(ImageType.Primary, 0)?.DateModified.Ticks.ToString("x"),
                IndexNumber = episode?.IndexNumber,
                ParentIndexNumber = episode?.ParentIndexNumber
            };
        }

        private void OnItemRemoved(object? sender, ItemChangeEventArgs e)
        {
            lock (_dataLock)
            {
                int removed = _notifications.RemoveAll(x => x.Id == e.Item.Id.ToString());
                if (removed > 0) _hasUnsavedChanges = true;
            }
        }

        private void SaveOnTick(object? state)
        {
            // Appelé par le Timer
            if (!_hasUnsavedChanges) return;
            ForceSaveNow();
        }

        private void ForceSaveNow()
        {
            lock (_saveLock)
            {
                try
                {
                    List<NotificationItem> copy;
                    // On copie rapidement les données pour libérer le lock principal
                    lock (_dataLock)
                    {
                        copy = _notifications.ToList();
                        _hasUnsavedChanges = false;
                    }

                    var json = JsonSerializer.Serialize(copy);
                    
                    // Écriture atomique (Safe Write)
                    var tempPath = _jsonPath + ".tmp";
                    File.WriteAllText(tempPath, json);
                    File.Move(tempPath, _jsonPath, true);
                }
                catch (Exception ex) { _logger.LogError(ex, "Erreur sauvegarde notifications."); }
            }
        }

        private void LoadNotifications()
        {
            lock (_dataLock)
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

        public List<NotificationItem> GetRecentNotifications()
        {
            lock (_dataLock) { return _notifications.ToList(); }
        }

        public void Dispose()
        {
            _bufferProcessTimer?.Dispose();
            _saveTimer?.Dispose();
            // Sauvegarde finale
            if (_hasUnsavedChanges) ForceSaveNow();

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
        public int? IndexNumber { get; set; } 
        public int? ParentIndexNumber { get; set; } 
    }
}