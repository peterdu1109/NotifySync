using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Runtime.CompilerServices; // Pour MethodImpl
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
        private readonly Lock _saveLock = new(); 
        private readonly ReaderWriterLockSlim _dataLock = new();
        
        private List<NotificationItem> _notifications = [];
        private long _versionCounter = DateTime.UtcNow.Ticks;

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

        // OPTIMISATION : Inlining pour cette méthode très fréquemment appelée
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string GetVersionHash()
        {
            return Interlocked.Read(ref _versionCounter).ToString();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateVersion()
        {
            Interlocked.Increment(ref _versionCounter);
        }

        private void PopulateInitialHistory()
        {
            try
            {
                var tempNotifs = new List<NotificationItem>();
                var typesToScan = new[] { BaseItemKind.Movie, BaseItemKind.Episode, BaseItemKind.MusicAlbum };
                var configCache = GetConfiguredCache();
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
                        var notif = CreateNotificationFromItem(item, configCache);
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
            if(e.Item is not null && !e.Item.IsVirtualItem) 
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
                var configCache = GetConfiguredCache();

                int processed = 0;
                while (processed < 100 && _eventBuffer.TryDequeue(out var item))
                {
                    try 
                    {
                        var notif = CreateNotificationFromItem(item, configCache);
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
                bool containsEpisodes = groupList.Exists(x => x.Type == "Episode");

                if (containsEpisodes)
                {
                    var seriesClusters = groupList
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
                        finalResults.AddRange(cluster.SeriesGroup);
                    }
                }
                else
                {
                    finalResults.AddRange(groupList.OrderByDescending(x => x.DateCreated).Take(limitPerCat));
                }
            }

            var sortedFinal = finalResults.OrderByDescending(x => x.DateCreated).ToList();

            _dataLock.EnterWriteLock();
            try
            {
                _notifications = sortedFinal;
                UpdateVersion(); 
            }
            finally
            {
                _dataLock.ExitWriteLock();
            }
        }

        private struct ConfigCache
        {
            public HashSet<Guid> Enabled;
            public HashSet<Guid> Manual;
            public Dictionary<Guid, string> Mappings;
            public List<string> ManualNames;
        }

        private ConfigCache GetConfiguredCache()
        {
            var config = Plugin.Instance?.Configuration;
            var c = new ConfigCache 
            { 
                Enabled = [], 
                Manual = [], 
                Mappings = [],
                ManualNames = [] 
            };

            if (config != null)
            {
                if (config.EnabledLibraries != null)
                {
                    foreach (var s in config.EnabledLibraries)
                        if (Guid.TryParse(s, out var g)) c.Enabled.Add(g);
                }
                if (config.ManualLibraryIds != null)
                {
                    foreach (var s in config.ManualLibraryIds)
                    {
                        if (Guid.TryParse(s, out var g)) c.Manual.Add(g);
                        else c.ManualNames.Add(s);
                    }
                }
                if (config.CategoryMappings != null)
                {
                    foreach (var m in config.CategoryMappings)
                    {
                        if (Guid.TryParse(m.LibraryId, out var g) && !string.IsNullOrEmpty(m.CategoryName))
                        {
                            c.Mappings[g] = m.CategoryName;
                        }
                    }
                }
            }
            return c;
        }

        private NotificationItem? CreateNotificationFromItem(BaseItem item, ConfigCache cache)
        {
            if (item is Folder || item.IsVirtualItem) return null;

            bool isMovie = item is MediaBrowser.Controller.Entities.Movies.Movie;
            bool isEpisode = item is MediaBrowser.Controller.Entities.TV.Episode;
            bool isMusic = item is MediaBrowser.Controller.Entities.Audio.MusicAlbum;

            if (!isMovie && !isEpisode && !isMusic) return null;

            bool hasRestrictions = cache.Enabled.Count > 0 || cache.Manual.Count > 0 || cache.ManualNames.Count > 0;
            string? matchedLibraryId = null;

            var rootLibrary = _libraryManager.GetCollectionFolders(item).FirstOrDefault();

            if (hasRestrictions)
            {
                if (rootLibrary != null)
                {
                    if (cache.Enabled.Contains(rootLibrary.Id) || cache.Manual.Contains(rootLibrary.Id))
                    {
                        matchedLibraryId = rootLibrary.Id.ToString();
                    }
                }

                if (matchedLibraryId == null)
                {
                    foreach (var parent in item.GetParents())
                    {
                        if (cache.Enabled.Contains(parent.Id) || cache.Manual.Contains(parent.Id))
                        {
                            matchedLibraryId = parent.Id.ToString();
                            break;
                        }
                        if (cache.ManualNames.Count > 0)
                        {
                            foreach(var name in cache.ManualNames)
                            {
                                if (string.Equals(parent.Name, name, StringComparison.OrdinalIgnoreCase))
                                {
                                    matchedLibraryId = parent.Id.ToString();
                                    break;
                                }
                            }
                            if (matchedLibraryId != null) break;
                        }
                    }
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

            if (matchedLibraryId != null && Guid.TryParse(matchedLibraryId, out var libGuid))
            {
                if (cache.Mappings.TryGetValue(libGuid, out var mappedName))
                {
                    category = mappedName;
                }
            }

            // OPTIMISATION : Lazy allocation pour éviter de créer des listes vides
            List<string>? backdropTags = null;
            var imgInfo = item.GetImageInfo(ImageType.Backdrop, 0);
            if (imgInfo != null)
            {
                // Syntaxe collection expression .NET 9
                backdropTags = [imgInfo.DateModified.Ticks.ToString("x")];
            }

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
                // Utilise le tableau vide statique si null, évite l'allocation new List<>()
                BackdropImageTags = backdropTags ?? [],
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
                    UpdateVersion();
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
                         UpdateVersion();
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
        
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SeriesName { get; set; }
        
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SeriesId { get; set; }
        
        public DateTime DateCreated { get; set; }
        public string Type { get; set; } = string.Empty;
        
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public long? RunTimeTicks { get; set; }
        
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? ProductionYear { get; set; }
        
        public List<string> BackdropImageTags { get; set; } = [];
        
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? PrimaryImageTag { get; set; }
        
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? IndexNumber { get; set; } 
        
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? ParentIndexNumber { get; set; } 
    }

    [JsonSerializable(typeof(List<NotificationItem>))]
    internal partial class NotificationJsonContext : JsonSerializerContext
    {
    }
}