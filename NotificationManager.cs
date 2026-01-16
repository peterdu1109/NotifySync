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
using System.Text.Json.Serialization;

namespace NotifySync
{
    public class NotificationManager : IDisposable
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger<NotificationManager> _logger;
        private readonly string _jsonPath;
        
        private readonly ConcurrentQueue<BaseItem> _eventBuffer = new();
        private readonly Timer _bufferProcessTimer;
        private readonly Timer _saveTimer;
        
        private volatile bool _hasUnsavedChanges = false;
        private readonly Lock _saveLock = new(); // .NET 9 Lock
        private readonly ReaderWriterLockSlim _dataLock = new();
        
        private List<NotificationItem> _notifications = [];
        private string _currentVersionHash = Guid.NewGuid().ToString(); 

        public static NotificationManager? Instance { get; private set; }

        public NotificationManager(ILibraryManager libraryManager, ILogger<NotificationManager> logger, IFileSystem fileSystem)
        {
            _libraryManager = libraryManager;
            _logger = logger;
            _jsonPath = Path.Combine(Plugin.Instance!.DataFolderPath, "notifications.json");
            Instance = this;
            
            LoadNotifications();
            
            _bufferProcessTimer = new Timer(ProcessBuffer, null, 2000, Timeout.Infinite);
            _saveTimer = new Timer(SaveOnTick, null, 10000, Timeout.Infinite);

            if (_notifications.Count == 0)
            {
                System.Threading.Tasks.Task.Run(PopulateInitialHistory);
            }

            _libraryManager.ItemAdded += OnItemAdded;
            _libraryManager.ItemRemoved += OnItemRemoved;
        }

        public void Refresh()
        {
            _dataLock.EnterWriteLock();
            try { _notifications.Clear(); }
            finally { _dataLock.ExitWriteLock(); }
            
            PopulateInitialHistory();
            ForceSaveNow(); 
        }

        public string GetVersionHash()
        {
            _dataLock.EnterReadLock();
            try { return _currentVersionHash; }
            finally { _dataLock.ExitReadLock(); }
        }

        private void PopulateInitialHistory()
        {
            try
            {
                var tempNotifs = new List<NotificationItem>();
                var typesToScan = new[] { BaseItemKind.Movie, BaseItemKind.Episode, BaseItemKind.MusicAlbum };
                
                int fetchLimit = 5000;

                foreach (var type in typesToScan)
                {
                    var query = new InternalItemsQuery
                    {
                        IncludeItemTypes = [type],
                        OrderBy = [(Jellyfin.Data.Enums.ItemSortBy.DateCreated, Jellyfin.Database.Implementations.Enums.SortOrder.Descending)],
                        Limit = fetchLimit, 
                        Recursive = true,
                        IsVirtualItem = false,
                        EnableTotalRecordCount = false
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
                try { _saveTimer.Change(10000, Timeout.Infinite); } catch {}
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur scan historique.");
            }
        }

        private void OnItemAdded(object? sender, ItemChangeEventArgs e)
        {
            if(e.Item != null && !e.Item.IsVirtualItem) 
            {
                _eventBuffer.Enqueue(e.Item);
                try { _bufferProcessTimer.Change(2000, Timeout.Infinite); } catch {}
            }
        }

        private void ProcessBuffer(object? state)
        {
            try
            {
                if (_eventBuffer.IsEmpty) return;

                var newItems = new List<NotificationItem>();
                // Limite le traitement par lot pour éviter de bloquer
                int processed = 0;
                while (processed < 100 && _eventBuffer.TryDequeue(out var item))
                {
                    try 
                    {
                        var notif = CreateNotificationFromItem(item);
                        if (notif != null) newItems.Add(notif);
                        processed++;
                    }
                    catch { }
                }

                if (newItems.Count > 0)
                {
                    ApplyCategoryQuotas(newItems);
                    _hasUnsavedChanges = true; 
                    try { _saveTimer.Change(10000, Timeout.Infinite); } catch {}
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur processing buffer");
            }
            finally
            {
                // Relance toujours le timer
                if (!_eventBuffer.IsEmpty)
                     try { _bufferProcessTimer.Change(1000, Timeout.Infinite); } catch {}
            }
        }

        private void ApplyCategoryQuotas(List<NotificationItem> incoming, bool isInitialLoad = false)
        {
            var config = Plugin.Instance?.Configuration;
            int limitPerCat = Math.Max(config?.MaxItems ?? 5, 1);

            List<NotificationItem> workingList;
            _dataLock.EnterReadLock();
            try 
            { 
                workingList = isInitialLoad ? [] : new List<NotificationItem>(_notifications);
            }
            finally { _dataLock.ExitReadLock(); }

            workingList.AddRange(incoming);

            var finalResults = new List<NotificationItem>(workingList.Count);
            var categoryGroups = workingList.GroupBy(n => n.Category);

            foreach (var catGroup in categoryGroups)
            {
                var groupList = catGroup.ToList();
                bool containsEpisodes = groupList.Any(x => x.Type == "Episode");

                if (containsEpisodes)
                {
                    var seriesClusters = groupList
                        .GroupBy(x => x.SeriesId ?? x.Name) 
                        .Select(g => new 
                        { 
                            SeriesGroup = g, 
                            LatestDate = g.Max(x => x.DateCreated) // Optimisé
                        })
                        .OrderByDescending(x => x.LatestDate)
                        .Take(limitPerCat); 

                    foreach (var cluster in seriesClusters)
                    {
                        finalResults.AddRange(cluster.SeriesGroup);
                    }
                }
                else
                {
                    finalResults.AddRange(groupList.OrderByDescending(x => x.DateCreated).Take(limitPerCat));
                }
            }

            var sortedFinal = finalResults.OrderByDescending(x => x.DateCreated).ToList();
            var newHash = sortedFinal.Count > 0 
                ? sortedFinal[0].DateCreated.Ticks.ToString() + "-" + sortedFinal.Count 
                : "empty";

            _dataLock.EnterWriteLock();
            try
            {
                _notifications = sortedFinal;
                _currentVersionHash = newHash;
            }
            finally
            {
                _dataLock.ExitWriteLock();
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

            bool hasRestrictions = (config.EnabledLibraries?.Count > 0) || (config.ManualLibraryIds?.Count > 0);
            string? matchedLibraryId = null;

            var rootLibrary = _libraryManager.GetCollectionFolders(item).FirstOrDefault();

            if (hasRestrictions)
            {
                var allParents = item.GetParents().ToList();
                if (rootLibrary != null && !allParents.Contains(rootLibrary)) allParents.Add(rootLibrary);

                var enabledIds = config.EnabledLibraries ?? [];
                var manualEntries = config.ManualLibraryIds ?? [];

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
            _dataLock.EnterWriteLock();
            try
            {
                string targetId = e.Item.Id.ToString();
                int removed = _notifications.RemoveAll(x => x.Id == targetId);
                if (removed > 0)
                {
                    _currentVersionHash = Guid.NewGuid().ToString();
                    _hasUnsavedChanges = true;
                    try { _saveTimer.Change(10000, Timeout.Infinite); } catch {}
                }
            }
            finally { _dataLock.ExitWriteLock(); }
        }

        private void SaveOnTick(object? state)
        {
            if (_hasUnsavedChanges) ForceSaveNow();
        }

        private void ForceSaveNow()
        {
            lock (_saveLock)
            {
                try
                {
                    List<NotificationItem> copy;
                    _dataLock.EnterReadLock();
                    try
                    {
                        copy = _notifications.ToList();
                        _hasUnsavedChanges = false;
                    }
                    finally { _dataLock.ExitReadLock(); }

                    var tempPath = _jsonPath + ".tmp";
                    using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        // Utilisation du contexte généré
                        JsonSerializer.Serialize(fs, copy, NotificationJsonContext.Default.ListNotificationItem);
                    }
                    
                    File.Move(tempPath, _jsonPath, true);
                }
                catch (Exception ex) { _logger.LogError(ex, "Erreur sauvegarde notifications."); }
            }
        }

        private void LoadNotifications()
        {
            _dataLock.EnterWriteLock();
            try
            {
                if (File.Exists(_jsonPath))
                {
                    using (var fs = new FileStream(_jsonPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        _notifications = JsonSerializer.Deserialize(fs, NotificationJsonContext.Default.ListNotificationItem) ?? [];
                    }

                    if (_notifications.Count > 0)
                         _currentVersionHash = _notifications[0].DateCreated.Ticks.ToString() + "-" + _notifications.Count;
                }
            }
            catch { _notifications = []; }
            finally { _dataLock.ExitWriteLock(); }
        }

        public List<NotificationItem> GetRecentNotifications()
        {
            _dataLock.EnterReadLock();
            try { return _notifications.ToList(); }
            finally { _dataLock.ExitReadLock(); }
        }

        public void Dispose()
        {
            _bufferProcessTimer?.Dispose();
            _saveTimer?.Dispose();
            if (_hasUnsavedChanges) ForceSaveNow();
            _dataLock?.Dispose();

            if (_libraryManager != null)
            {
                _libraryManager.ItemAdded -= OnItemAdded;
                _libraryManager.ItemRemoved -= OnItemRemoved;
            }
            GC.SuppressFinalize(this);
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
        public List<string> BackdropImageTags { get; set; } = [];
        public string? PrimaryImageTag { get; set; }
        public int? IndexNumber { get; set; } 
        public int? ParentIndexNumber { get; set; } 
    }

    [JsonSerializable(typeof(List<NotificationItem>))]
    internal partial class NotificationJsonContext : JsonSerializerContext
    {
    }
}