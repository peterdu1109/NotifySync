using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;

namespace NotifySync
{
    /// <summary>
    /// Manages notifications and their persistence in SQLite.
    /// </summary>
    public sealed class NotificationManager : IDisposable
    {
        private const int GlobalRetentionLimit = 2000;
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger<NotificationManager> _logger;
        private readonly string _jsonPath;
        private readonly NotificationDatabase _db;
        private readonly ConcurrentQueue<BaseItem> _eventBuffer = new ();
        private readonly Timer _bufferProcessTimer;
        private readonly ReaderWriterLockSlim _dataLock = new ();
        private readonly CancellationTokenSource _disposeCts = new ();
        private List<NotificationItem> _notifications = new List<NotificationItem>();
        private long _versionCounter = DateTime.UtcNow.Ticks;
        private int _isProcessingBuffer;

        /// <summary>
        /// Initializes a new instance of the <see cref="NotificationManager"/> class.
        /// </summary>
        /// <param name="libraryManager">The library manager.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="fileSystem">The file system.</param>
        public NotificationManager(ILibraryManager libraryManager, ILogger<NotificationManager> logger, IFileSystem fileSystem)
        {
            _libraryManager = libraryManager;
            _logger = logger;
            _jsonPath = Path.Combine(Plugin.Instance!.DataFolderPath, "notifications.json");

            _db = new NotificationDatabase(Plugin.Instance!.DataFolderPath, _logger);
            Instance = this;

            LoadAndMigrate();

            _bufferProcessTimer = new Timer(ProcessBuffer, null, 2000, Timeout.Infinite);

            _libraryManager.ItemAdded += OnItemAdded;
            _libraryManager.ItemRemoved += OnItemRemoved;
            _libraryManager.ItemUpdated += OnItemUpdated;
        }

        /// <summary>
        /// Gets the singleton instance.
        /// </summary>
        public static NotificationManager? Instance { get; private set; }

        /// <summary>
        /// Triggers a manual history scan.
        /// </summary>
        /// <param name="progress">The progress object.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public void ManualHistoryScan(IProgress<double> progress, CancellationToken cancellationToken)
        {
            PopulateInitialHistory(progress, cancellationToken);
        }

        /// <summary>
        /// Gets the current version hash.
        /// </summary>
        /// <returns>A string representation of the version.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string GetVersionHash()
        {
            return Interlocked.Read(ref _versionCounter).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Returns all recent notifications.
        /// </summary>
        /// <returns>A collection of notification items.</returns>
        public IReadOnlyCollection<NotificationItem> GetRecentNotifications()
        {
            try
            {
                _dataLock.EnterReadLock();
                return _notifications.ConvertAll(n => n.Clone());
            }
            finally
            {
                if (_dataLock.IsReadLockHeld)
                {
                    _dataLock.ExitReadLock();
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _disposeCts.Cancel();
            _bufferProcessTimer?.Dispose();
            _dataLock?.Dispose();
            _disposeCts.Dispose();
            _db?.Dispose();

            if (_libraryManager != null)
            {
                _libraryManager.ItemAdded -= OnItemAdded;
                _libraryManager.ItemRemoved -= OnItemRemoved;
                _libraryManager.ItemUpdated -= OnItemUpdated;
            }

            GC.SuppressFinalize(this);
        }

        private void LoadAndMigrate()
        {
            var diskNotifs = _db.GetAllNotifications().ToList();

            if (diskNotifs.Count == 0 && File.Exists(_jsonPath))
            {
                _logger.LogInformation("Migration des notifications JSON vers SQLite détectée...");
                try
                {
                    using (var fs = new FileStream(_jsonPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        var oldNotifs = JsonSerializer.Deserialize(fs, PluginJsonContext.Default.ListNotificationItem) ?? new List<NotificationItem>();
                        if (oldNotifs.Count > 0)
                        {
                            _db.SaveNotifications(oldNotifs);
                            diskNotifs = oldNotifs;
                            _logger.LogInformation("{Count} notifications migrées avec succès.", oldNotifs.Count);
                        }
                    }

                    File.Move(_jsonPath, _jsonPath + ".bak");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erreur lors de la migration JSON vers SQLite.");
                }
            }

            // Enforce Quota even on startup to trim any bloat from before
            var config = Plugin.Instance?.Configuration;
            int maxItems = config?.MaxItems ?? 10;
            var quotaResult = ApplyCategoryQuotas(diskNotifs, maxItems);
            var finalNotifications = quotaResult.Kept;
            var itemsToDelete = quotaResult.RemovedIds;

            var newNotifs = finalNotifications.OrderByDescending(n => n.DateCreated).ToList();

            if (itemsToDelete.Count > 0)
            {
                _db.DeleteNotifications(itemsToDelete);
                _db.Vacuum(); // Optimize Db after startup trim
            }

            try
            {
                _dataLock.EnterWriteLock();
                _notifications = newNotifs;
            }
            finally
            {
                if (_dataLock.IsWriteLockHeld)
                {
                    _dataLock.ExitWriteLock();
                }
            }
        }

        private void OnItemAdded(object? sender, ItemChangeEventArgs e)
        {
            if (e.Item == null || (e.Item.GetType().Name != "Movie" && e.Item.GetType().Name != "Episode"))
            {
                return;
            }

            _eventBuffer.Enqueue(e.Item);
            _bufferProcessTimer.Change(500, Timeout.Infinite);
        }

        private void OnItemRemoved(object? sender, ItemChangeEventArgs e)
        {
            if (e.Item == null)
            {
                return;
            }

            bool dbNeedsUpdate = false;
            try
            {
                _dataLock.EnterWriteLock();
                int removed = _notifications.RemoveAll(n => n.Id == e.Item.Id.ToString());
                if (removed > 0)
                {
                    dbNeedsUpdate = true;
                    Interlocked.Exchange(ref _versionCounter, DateTime.UtcNow.Ticks);
                }
            }
            finally
            {
                if (_dataLock.IsWriteLockHeld)
                {
                    _dataLock.ExitWriteLock();
                }
            }

            if (dbNeedsUpdate)
            {
                _db.DeleteNotifications(new[] { e.Item.Id.ToString() });
            }
        }

        private void OnItemUpdated(object? sender, ItemChangeEventArgs e)
        {
            // Update logic can be added here if needed.
        }

        private void ProcessBuffer(object? state)
        {
            if (Interlocked.CompareExchange(ref _isProcessingBuffer, 1, 0) != 0)
            {
                return;
            }

            try
            {
                var newItems = new List<NotificationItem>();
                while (_eventBuffer.TryDequeue(out var item))
                {
                    if (item == null)
                    {
                        continue;
                    }

                    var notif = CreateNotificationFromItem(item);
                    if (notif != null)
                    {
                        newItems.Add(notif);
                    }
                }

                if (newItems.Count > 0)
                {
                    var config = Plugin.Instance?.Configuration;
                    int maxItems = config?.MaxItems ?? 10;
                    var itemsToDelete = new List<string>();
                    var itemsToSave = new List<NotificationItem>(newItems);

                    try
                    {
                        _dataLock.EnterWriteLock();
                        foreach (var ni in itemsToSave)
                        {
                            _notifications.RemoveAll(n => n.Id == ni.Id);
                            _notifications.Insert(0, ni);
                        }

                        // Apply Quota per category
                        var quotaResult = ApplyCategoryQuotas(_notifications, maxItems);
                        var finalNotifications = quotaResult.Kept;
                        itemsToDelete.AddRange(quotaResult.RemovedIds);

                        _notifications = finalNotifications.OrderByDescending(n => n.DateCreated).ToList();

                        if (_notifications.Count > GlobalRetentionLimit)
                        {
                            var overLimit = _notifications.Skip(GlobalRetentionLimit).Select(n => n.Id).ToList();
                            itemsToDelete.AddRange(overLimit!);
                            _notifications = _notifications.Take(GlobalRetentionLimit).ToList();
                        }

                        Interlocked.Exchange(ref _versionCounter, DateTime.UtcNow.Ticks);
                    }
                    finally
                    {
                        if (_dataLock.IsWriteLockHeld)
                        {
                            _dataLock.ExitWriteLock();
                        }
                    }

                    // IO outside lock
                    _db.SaveNotifications(itemsToSave);
                    if (itemsToDelete.Count > 0)
                    {
                        _db.DeleteNotifications(itemsToDelete);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du traitement du buffer de notifications.");
            }
            finally
            {
                Interlocked.Exchange(ref _isProcessingBuffer, 0);
            }
        }

        private void PopulateInitialHistory(IProgress<double> progress, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Lancement du scan manuel de l'historique NotifySync...");

            var config = Plugin.Instance?.Configuration;
            int maxItems = config?.MaxItems ?? 10;

            // diagnostic
            if (config != null)
            {
                var enabledStr = config.EnabledLibraries != null ? string.Join(", ", config.EnabledLibraries) : "null";
                var manualStr = config.ManualLibraryIds != null ? string.Join(", ", config.ManualLibraryIds) : "null";
                _logger.LogInformation("NotifySync Config: EnabledLibraries=[{Libs}], ManualLibraryIds=[{Manual}]", enabledStr, manualStr);
            }

            var queries = new[]
            {
                new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Movie },
                    Recursive = true,
                    OrderBy = new[] { (ItemSortBy.DateCreated, (Jellyfin.Database.Implementations.Enums.SortOrder)1) },
                    Limit = maxItems * 100, // Enough depth to find unique items across multi-libraries
                    DtoOptions = new MediaBrowser.Controller.Dto.DtoOptions(false)
                },
                new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Episode },
                    Recursive = true,
                    OrderBy = new[] { (ItemSortBy.DateCreated, (Jellyfin.Database.Implementations.Enums.SortOrder)1) },
                    Limit = maxItems * 400, // High depth for episodes mapping to same category globally (e.g. Animés + Séries)
                    DtoOptions = new MediaBrowser.Controller.Dto.DtoOptions(false)
                },
                new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { BaseItemKind.Audio },
                    Recursive = true,
                    OrderBy = new[] { (ItemSortBy.DateCreated, (Jellyfin.Database.Implementations.Enums.SortOrder)1) },
                    Limit = maxItems * 400, // Dozens of tracks per album
                    DtoOptions = new MediaBrowser.Controller.Dto.DtoOptions(false)
                }
            };

            var items = new List<BaseItem>();
            foreach (var q in queries)
            {
                items.AddRange(_libraryManager.GetItemList(q));
            }

            // Re-sort everything combined globally by DateCreated Descending
            items = items.OrderByDescending(i => i.DateCreated).ToList();

            var results = new List<NotificationItem>();
            int count = 0;
            int skippedNotEnabled = 0;
            int skippedNull = 0;
            var typeCounts = new Dictionary<string, int>();

            var categoryCounts = new Dictionary<string, int>();
            // Track unique series per category to count series, not episodes
            var categorySeriesIds = new Dictionary<string, HashSet<string>>();

            _logger.LogInformation("NotifySync Scan: {Total} items returned by combined library queries.", items.Count);

            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Track type distribution
                var typeName = item.GetType().Name;
                typeCounts[typeName] = typeCounts.GetValueOrDefault(typeName) + 1;

                var notif = CreateNotificationFromItem(item);
                if (notif != null)
                {
                    // For episodes and audio albums: count unique series/albums, not individual tracks
                    // For movies/other: count individual items
                    bool isEpisodeOrAlbum = !string.IsNullOrEmpty(notif.SeriesId);
                    string categoryKey = notif.Category;

                    if (!categorySeriesIds.TryGetValue(categoryKey, out var seriesSet))
                    {
                        seriesSet = new HashSet<string>();
                        categorySeriesIds[categoryKey] = seriesSet;
                    }

                    if (!categoryCounts.TryGetValue(categoryKey, out int currentCount))
                    {
                        currentCount = 0;
                    }

                    if (isEpisodeOrAlbum)
                    {
                        // For episodes: allow if we haven't reached maxItems unique series yet
                        bool isNewSeries = !seriesSet.Contains(notif.SeriesId!);
                        if (isNewSeries && currentCount >= maxItems)
                        {
                            // Already have enough unique series, skip this new series
                        }
                        else
                        {
                            results.Add(notif);
                            if (isNewSeries)
                            {
                                seriesSet.Add(notif.SeriesId!);
                                categoryCounts[categoryKey] = currentCount + 1;
                            }
                        }
                    }
                    else
                    {
                        // For movies: count individually as before
                        if (currentCount < maxItems)
                        {
                            results.Add(notif);
                            categoryCounts[categoryKey] = currentCount + 1;
                        }
                    }
                }
                else
                {
                    if (!IsItemInEnabledLibrary(item))
                    {
                        skippedNotEnabled++;
                    }
                    else
                    {
                        skippedNull++;
                    }
                }

                count++;
                progress?.Report((double)count / items.Count * 100);
            }

            // Log diagnostics
            _logger.LogInformation("NotifySync Scan Diagnostics: Types found: {Types}", string.Join(", ", typeCounts.Select(kv => $"{kv.Key}={kv.Value}")));
            _logger.LogInformation("NotifySync Scan Diagnostics: Categories Reach: {Categories}", string.Join(", ", categoryCounts.Select(kv => $"{kv.Key}={kv.Value}")));
            _logger.LogInformation("NotifySync Scan Diagnostics: Skipped (not in enabled library): {Skipped}, Skipped (null/error): {Null}", skippedNotEnabled, skippedNull);

            var newNotifs = results.OrderByDescending(n => n.DateCreated).ToList();
            var oldDbIds = new List<string>();

            try
            {
                _dataLock.EnterReadLock();
                oldDbIds = _notifications.Select(n => n.Id).ToList();
            }
            finally
            {
                if (_dataLock.IsReadLockHeld)
                {
                    _dataLock.ExitReadLock();
                }
            }

            // Discard old, insert new into DB directly
            _db.DeleteNotifications(oldDbIds!);
            _db.SaveNotifications(newNotifs);
            _db.Vacuum(); // Reclaim space after mass delete/insert

            try
            {
                _dataLock.EnterWriteLock();
                _notifications = newNotifs;
                Interlocked.Exchange(ref _versionCounter, DateTime.UtcNow.Ticks);
                _logger.LogInformation("Scan terminé. {Count} items indexés.", _notifications.Count);
            }
            finally
            {
                if (_dataLock.IsWriteLockHeld)
                {
                    _dataLock.ExitWriteLock();
                }
            }
        }

        private bool IsItemInEnabledLibrary(BaseItem item)
        {
            // Universally filter out virtual/ghost items (uninstalled plugins, missing metadata episodes)
            if (item.IsVirtualItem)
            {
                return false;
            }

            var config = Plugin.Instance?.Configuration;
            if (config == null)
            {
                return false;
            }

            // If no libraries are explicitly checked AND no manual IDs
            // Apply streaming URL filter only here (when nothing is explicitly configured)
            if ((config.EnabledLibraries == null || config.EnabledLibraries.Count == 0) &&
                (config.ManualLibraryIds == null || config.ManualLibraryIds.Count == 0))
            {
                // Block streaming/channel items when no explicit library is selected
                if (!string.IsNullOrEmpty(item.Path))
                {
                    if (item.Path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
                        item.Path.Contains("channels", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }

                // If category mappings exist, use their LibraryIds as implicit enabled libraries
                if (config.CategoryMappings != null && config.CategoryMappings.Count > 0)
                {
                    var mapOwners = item.GetAncestorIds().ToArray();
                    foreach (var map in config.CategoryMappings)
                    {
                        if (Guid.TryParse(map.LibraryId, out var mapGuid) && mapOwners.Contains(mapGuid))
                        {
                            return true;
                        }
                    }

                    return false;
                }

                // No config at all (backward compat): allow all local library items
                return true;
            }

            var owners = item.GetAncestorIds().ToArray();

            // Check EnabledLibraries Checkboxes
            if (config.EnabledLibraries != null)
            {
                foreach (var libId in config.EnabledLibraries)
                {
                    if (Guid.TryParse(libId, out var libGuid) && owners.Contains(libGuid))
                    {
                        return true;
                    }
                }
            }

            // Check ManualLibraryIds (can be ID or plain Name)
            if (config.ManualLibraryIds != null && config.ManualLibraryIds.Count > 0)
            {
                foreach (var manualId in config.ManualLibraryIds)
                {
                    if (Guid.TryParse(manualId, out var manualGuid) && owners.Contains(manualGuid))
                    {
                        return true;
                    }

                    // Allow exact name matching for folders by checking the ancestors names through the library manager
                    // Since owners contains IDs, let's just query the manager
                    if (_libraryManager != null)
                    {
                        foreach (var ownerId in owners)
                        {
                            var ownerItem = _libraryManager.GetItemById(ownerId);
                            if (ownerItem != null && ownerItem.Name != null && ownerItem.Name.Equals(manualId, StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        private NotificationItem? CreateNotificationFromItem(BaseItem item)
        {
            if (!IsItemInEnabledLibrary(item))
            {
                return null;
            }

            try
            {
                string category = "Autres";
                var owners = item.GetAncestorIds().ToArray();
                var config = Plugin.Instance?.Configuration;

                if (config != null)
                {
                    foreach (var map in config.CategoryMappings)
                    {
                        if (Guid.TryParse(map.LibraryId, out var libGuid) && owners.Contains(libGuid))
                        {
                            category = map.CategoryName;
                            break;
                        }
                    }
                }

                var notif = new NotificationItem
                {
                    Id = item.Id.ToString(),
                    Name = item.Name,
                    Category = category,
                    SeriesName = (item as Episode)?.SeriesName,
                    SeriesId = (item as Episode)?.SeriesId.ToString(),
                    DateCreated = item.DateCreated,
                    Type = item.GetType().Name,
                    RunTimeTicks = item.RunTimeTicks,
                    ProductionYear = item.ProductionYear,
                    BackdropImageTags = item.ImageInfos.Where(i => i.Type == ImageType.Backdrop).Select(i => i.DateModified.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList(),
                    PrimaryImageTag = item.ImageInfos.Where(i => i.Type == ImageType.Primary).Select(i => i.DateModified.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)).FirstOrDefault(),
                    IndexNumber = item.IndexNumber,
                    ParentIndexNumber = item.ParentIndexNumber
                };

                if (item.GetBaseItemKind() == BaseItemKind.Audio && item is MediaBrowser.Controller.Entities.Audio.Audio audioItem)
                {
                    notif.SeriesName = audioItem.Album;
                    notif.SeriesId = audioItem.ParentId.ToString();
                }

                return notif;
            }
            catch
            {
                return null;
            }
        }

        private (List<NotificationItem> Kept, List<string> RemovedIds) ApplyCategoryQuotas(List<NotificationItem> sourceList, int maxItems)
        {
            var categorized = sourceList.GroupBy(n => n.Category).ToList();
            var finalNotifications = new List<NotificationItem>();
            var itemsToDelete = new List<string>();

            foreach (var group in categorized)
            {
                var sorted = group.OrderByDescending(n => n.DateCreated).ToList();
                var categorySeriesIds = new HashSet<string>();
                int currentCount = 0;

                foreach (var item in sorted)
                {
                    bool isEpisode = !string.IsNullOrEmpty(item.SeriesId);
                    bool keep = false;

                    if (isEpisode)
                    {
                        bool isNewSeries = !categorySeriesIds.Contains(item.SeriesId!);
                        if (isNewSeries && currentCount >= maxItems)
                        {
                            // Already have max unique series, do not keep
                        }
                        else
                        {
                            keep = true;
                            if (isNewSeries)
                            {
                                categorySeriesIds.Add(item.SeriesId!);
                                currentCount++;
                            }
                        }
                    }
                    else
                    {
                        if (currentCount < maxItems)
                        {
                            keep = true;
                            currentCount++;
                        }
                    }

                    if (keep)
                    {
                        finalNotifications.Add(item);
                    }
                    else
                    {
                        itemsToDelete.Add(item.Id);
                    }
                }
            }

            return (finalNotifications, itemsToDelete);
        }
    }
}
