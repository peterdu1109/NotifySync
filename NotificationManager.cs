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
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;

namespace NotifySync
{
    /// <summary>
    /// Manages notifications and their persistence in SQLite.
    /// </summary>
    public sealed class NotificationManager : IDisposable
    {
        private const int DatabaseCategoryLimit = 100;
        private const int GlobalRetentionLimit = 2000;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserDataManager _userDataManager;
        private readonly ILogger<NotificationManager> _logger;
        private readonly string _jsonPath;
        private readonly string _clearedPath;
        private readonly NotificationDatabase _db;
        private readonly ConcurrentDictionary<string, long> _userClearedCache = new ();
        private readonly object _clearedLock = new ();
        private readonly ConcurrentQueue<BaseItem> _eventBuffer = new ();
        private readonly Timer _bufferProcessTimer;
        private readonly ReaderWriterLockSlim _dataLock = new ();
        private readonly CancellationTokenSource _disposeCts = new ();

        private readonly ConcurrentDictionary<string, long> _userStateVersion = new ();
        private readonly ConcurrentDictionary<string, DateTime> _recentlyRemovedIds = new ();
        private int _isClearedDirty;
        private List<NotificationItem> _notifications = new List<NotificationItem>();
        private long _versionCounter = DateTime.UtcNow.Ticks;
        private int _isProcessingBuffer;
        private int _isDisposed;
        private long _lastPurgeTicks;

        /// <summary>
        /// Initializes a new instance of the <see cref="NotificationManager"/> class.
        /// </summary>
        /// <param name="libraryManager">The library manager.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="userDataManager">The user data manager.</param>
        public NotificationManager(ILibraryManager libraryManager, ILogger<NotificationManager> logger, IUserDataManager userDataManager)
        {
            _libraryManager = libraryManager;
            _logger = logger;
            _userDataManager = userDataManager;

            // Stable data path to avoid losing data when plugin version (folder name) changes
            var stableDataPath = Plugin.Instance!.PluginDataFolderPath;
            Directory.CreateDirectory(stableDataPath);

            _jsonPath = Path.Combine(stableDataPath, "notifications.json");
            _clearedPath = Path.Combine(stableDataPath, "users_cleared.json");
            _logger.LogDebug("NotifySync: Data paths — DataPath={Path}, ClearedPath={ClearedPath}.", stableDataPath, _clearedPath);

            _db = new NotificationDatabase(stableDataPath, _logger);
            LoadUserCleared();
            Instance = this;

            LoadAndMigrate();

            _bufferProcessTimer = new Timer(ProcessBuffer, null, 2000, Timeout.Infinite);

            _libraryManager.ItemAdded += OnItemAdded;
            _libraryManager.ItemRemoved += OnItemRemoved;
            _libraryManager.ItemUpdated += OnItemUpdated;
            _userDataManager.UserDataSaved += OnUserDataSaved;
        }

        /// <summary>
        /// Gets the singleton instance.
        /// </summary>
        public static NotificationManager? Instance { get; private set; }

        /// <summary>
        /// Gets the notification database.
        /// </summary>
        public NotificationDatabase Db => _db;

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
        /// Gets the current version hash, optionally including per-user state version.
        /// </summary>
        /// <param name="normalizedUserId">The normalized user ID to include user-specific state, or null for global only.</param>
        /// <returns>A string representation of the version.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string GetVersionHash(string? normalizedUserId = null)
        {
            var global = Interlocked.Read(ref _versionCounter).ToString(CultureInfo.InvariantCulture);
            if (normalizedUserId != null && _userStateVersion.TryGetValue(normalizedUserId, out var userVer))
            {
                return global + "_" + userVer.ToString(CultureInfo.InvariantCulture);
            }

            return global;
        }

        /// <summary>
        /// Increments the user-specific state version (called after MarkRead/Dismiss).
        /// </summary>
        /// <param name="normalizedUserId">The normalized user identifier.</param>
        public void IncrementUserStateVersion(string normalizedUserId)
        {
            _userStateVersion.AddOrUpdate(normalizedUserId, 1, (_, v) => v + 1);
        }

        /// <summary>
        /// Returns the IDs of all notifications matching a given item or series ID (without cloning).
        /// Used by the Dismiss endpoint to resolve group dismissals efficiently.
        /// </summary>
        /// <param name="itemId">The item ID or series ID to match.</param>
        /// <returns>A list of matching notification IDs.</returns>
        public IReadOnlyList<string> ResolveNotificationIds(string itemId)
        {
            try
            {
                _dataLock.EnterReadLock();
                var ids = _notifications
                    .Where(n => n.Id == itemId || n.SeriesId == itemId)
                    .Select(n => n.Id)
                    .Distinct()
                    .ToList();
                return ids.Count > 0 ? ids : new List<string> { itemId };
            }
            finally
            {
                if (_dataLock.IsReadLockHeld)
                {
                    _dataLock.ExitReadLock();
                }
            }
        }

        /// <summary>
        /// Returns all recent notifications as read-only references (no cloning).
        /// Callers that need mutable copies should clone only the items they keep.
        /// </summary>
        /// <returns>A snapshot list of notification item references.</returns>
        public IReadOnlyList<NotificationItem> GetRecentNotifications()
        {
            try
            {
                _dataLock.EnterReadLock();
                return _notifications.ToList();
            }
            finally
            {
                if (_dataLock.IsReadLockHeld)
                {
                    _dataLock.ExitReadLock();
                }
            }
        }

        /// <summary>
        /// Gets the cleared timestamp for a user.
        /// </summary>
        /// <param name="userId">The user unique identifier.</param>
        /// <returns>The ticks until which notifications are cleared.</returns>
        public long GetUserCleared(string userId)
        {
            var normalized = NormalizeUserId(userId);
            return _userClearedCache.TryGetValue(normalized, out var ts) ? ts : 0;
        }

        /// <summary>
        /// Sets the cleared timestamp for a user and persists it.
        /// </summary>
        /// <param name="userId">The user unique identifier.</param>
        /// <param name="timestamp">The ticks timestamp to set.</param>
        public void SetUserCleared(string userId, long timestamp)
        {
            var normalized = NormalizeUserId(userId);
            _userClearedCache[normalized] = timestamp;
            Interlocked.Exchange(ref _isClearedDirty, 1);
            SaveUserCleared();
            _logger.LogDebug("NotifySync: User {User} cleared until {Ts}.", normalized, timestamp);
        }

        private string NormalizeUserId(string userId) => IdHelper.NormalizeId(userId);

        private void LoadUserCleared()
        {
            if (!File.Exists(_clearedPath))
            {
                return;
            }

            try
            {
                var json = File.ReadAllText(_clearedPath);
                var data = JsonSerializer.Deserialize(json, PluginJsonContext.Default.DictionaryStringInt64);
                if (data != null)
                {
                    foreach (var kvp in data)
                    {
                        var normalized = NormalizeUserId(kvp.Key);
                        _userClearedCache[normalized] = kvp.Value;
                    }

                    _logger.LogDebug("NotifySync: Cleared state loaded for {Count} users.", data.Count);
                }
                else
                {
                    _logger.LogWarning("NotifySync: Deserialization returned null for {Path}.", _clearedPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotifySync: Error loading users_cleared.json from {Path}.", _clearedPath);
            }
        }

        private void SaveUserCleared()
        {
            lock (_clearedLock)
            {
                if (Interlocked.CompareExchange(ref _isClearedDirty, 0, 1) == 0)
                {
                    return;
                }

                try
                {
                    var snapshot = _userClearedCache.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                    var json = JsonSerializer.Serialize(snapshot, PluginJsonContext.Default.DictionaryStringInt64);
                    File.WriteAllText(_clearedPath + ".tmp", json);
                    File.Move(_clearedPath + ".tmp", _clearedPath, true);
                    _logger.LogDebug("NotifySync: Cleared state saved for {Count} users.", snapshot.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "NotifySync: Error saving users_cleared.json.");
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
            {
                return;
            }

            SaveUserCleared();
            _disposeCts.Cancel();
            _bufferProcessTimer?.Dispose();
            _dataLock?.Dispose();
            _disposeCts.Dispose();
            _db?.Dispose();

            _libraryManager.ItemAdded -= OnItemAdded;
            _libraryManager.ItemRemoved -= OnItemRemoved;
            _libraryManager.ItemUpdated -= OnItemUpdated;
            _userDataManager.UserDataSaved -= OnUserDataSaved;

            GC.SuppressFinalize(this);
        }

        private void LoadAndMigrate()
        {
            var diskNotifs = _db.GetAllNotifications().ToList();
            _logger.LogInformation("NotifySync Startup: {Count} notifications loaded from SQLite database.", diskNotifs.Count);

            if (diskNotifs.Count == 0 && File.Exists(_jsonPath))
            {
                _logger.LogInformation("JSON to SQLite notification migration detected...");
                try
                {
                    using (var fs = new FileStream(_jsonPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        var oldNotifs = JsonSerializer.Deserialize(fs, PluginJsonContext.Default.ListNotificationItem) ?? new List<NotificationItem>();
                        if (oldNotifs.Count > 0)
                        {
                            _db.SaveNotifications(oldNotifs);
                            diskNotifs = oldNotifs;
                            _logger.LogInformation("{Count} notifications migrated successfully.", oldNotifs.Count);
                        }
                    }

                    File.Move(_jsonPath, _jsonPath + ".bak");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during JSON to SQLite migration.");
                }
            }

            // Enforce Quota even on startup to trim any bloat from before
            var quotaResult = CategoryQuotaService.ApplyCategoryQuotas(diskNotifs, DatabaseCategoryLimit);
            var finalNotifications = quotaResult.Kept;
            var itemsToDelete = quotaResult.RemovedIds;

            var newNotifs = finalNotifications.OrderByDescending(n => n.DateCreated).ToList();
            _logger.LogInformation("NotifySync Startup: {Count} notifications kept after applying quotas ({Deleted} removed).", newNotifs.Count, itemsToDelete.Count);

            if (itemsToDelete.Count > 0)
            {
                _db.DeleteNotifications(itemsToDelete);
            }

            // Purge orphaned user states on startup
            _db.PurgeOrphanedStates();

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

        private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
        {
            // Invalidate cache on ANY user data change (played, unplayed, season-level, etc.)
            // This ensures the bell updates when:
            //   - An item is marked as watched (disappears from bell)
            //   - An item is unmarked (reappears in bell)
            //   - A whole season is toggled (propagates to episodes)
            Interlocked.Increment(ref _versionCounter);
            NotifyController.InvalidateUserCache(e.UserId.ToString("N"));
        }

        private void OnItemAdded(object? sender, ItemChangeEventArgs e)
        {
            if (e.Item == null || (e.Item.GetType().Name != "Movie" && e.Item.GetType().Name != "Episode" && e.Item.GetType().Name != "Audio"))
            {
                return;
            }

            _eventBuffer.Enqueue(e.Item);
            if (!_disposeCts.IsCancellationRequested)
            {
                try
                {
                    _bufferProcessTimer.Change(500, Timeout.Infinite);
                }
                catch (ObjectDisposedException)
                {
                    // Timer already disposed during plugin shutdown — safe to ignore
                }
            }
        }

        private void OnItemRemoved(object? sender, ItemChangeEventArgs e)
        {
            if (e.Item == null)
            {
                return;
            }

            // Log deleted item if tracking is enabled
            var config = Plugin.Instance?.Configuration;
            if (config != null && config.EnableDeletedTracking && !e.Item.IsFolder && !(e.Item is Folder))
            {
                try
                {
                    var item = e.Item;
                    string type = item.GetType().Name;
                    string? seriesName = (item as Episode)?.SeriesName;
                    int? year = item.ProductionYear;
                    int? indexNum = item.IndexNumber;
                    int? parentIndexNum = item.ParentIndexNumber;

                    _db.SaveDeletedItem(item.Id.ToString(), item.Name ?? "Unknown", type, seriesName, year, indexNum, parentIndexNum);

                    // Purge at most once per day to avoid unnecessary DB writes
                    var nowTicks = DateTime.UtcNow.Ticks;
                    var lastPurge = Interlocked.Read(ref _lastPurgeTicks);
                    if ((nowTicks - lastPurge) > TimeSpan.TicksPerDay)
                    {
                        Interlocked.Exchange(ref _lastPurgeTicks, nowTicks);
                        _db.PurgeExpiredDeletedItems(config.DeletedRetentionDays > 0 ? config.DeletedRetentionDays : 30);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "NotifySync: Error tracking deletion of {Name}.", e.Item.Name);
                }
            }

            var itemId = e.Item.Id.ToString();
            bool dbNeedsUpdate = false;
            try
            {
                _dataLock.EnterWriteLock();
                int removed = _notifications.RemoveAll(n => n.Id == itemId);
                if (removed > 0)
                {
                    // Track recently removed notification IDs for upgrade detection.
                    // When Radarr/Sonarr upgrades a file, Jellyfin fires ItemRemoved then
                    // ItemAdded/ItemUpdated. By remembering the removed ID, we can detect
                    // the re-appearing item as an upgrade rather than a new addition.
                    _recentlyRemovedIds[itemId] = DateTime.UtcNow;

                    dbNeedsUpdate = true;
                    Interlocked.Increment(ref _versionCounter);
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
                _db.DeleteNotifications(new[] { itemId });
                _db.DeleteStatesForNotification(itemId);
            }

            // Purge stale entries older than 10 minutes
            var cutoff = DateTime.UtcNow.AddMinutes(-10);
            foreach (var kvp in _recentlyRemovedIds)
            {
                if (kvp.Value < cutoff)
                {
                    _recentlyRemovedIds.TryRemove(kvp.Key, out _);
                }
            }
        }

        private void OnItemUpdated(object? sender, ItemChangeEventArgs e)
        {
            if (e.Item == null)
            {
                return;
            }

            bool dbNeedsUpdate = false;
            NotificationItem? updatedNotif = null;
            bool isGhostItemRescue = false;

            try
            {
                _dataLock.EnterWriteLock();
                var existingIndex = _notifications.FindIndex(n => n.Id == e.Item.Id.ToString());
                if (existingIndex >= 0)
                {
                    var existing = _notifications[existingIndex];
                    updatedNotif = CreateNotificationFromItem(e.Item);
                    if (updatedNotif != null)
                    {
                        // Detect file replacement (quality upgrade)
                        // Primary: Path changed = file replaced (strongest indicator)
                        // Fallback: Size + DateModified changed = file replaced
                        bool pathChanged = !string.IsNullOrEmpty(existing.FilePath)
                            && !string.IsNullOrEmpty(updatedNotif.FilePath)
                            && !string.Equals(existing.FilePath, updatedNotif.FilePath, StringComparison.Ordinal);
                        bool sizeChanged = existing.Size.HasValue
                            && updatedNotif.Size.HasValue
                            && existing.Size.Value != updatedNotif.Size.Value;
                        bool dateChanged = existing.DateModifiedTicks.HasValue
                            && updatedNotif.DateModifiedTicks.HasValue
                            && existing.DateModifiedTicks.Value != updatedNotif.DateModifiedTicks.Value;
                        // Fallback: if existing has no file metadata (pre-5.5.7.3), check deleted history
                        bool legacyFallback = string.IsNullOrEmpty(existing.FilePath)
                            && !existing.Size.HasValue
                            && _db.HasRecentDeletedMatch(
                                updatedNotif.Name,
                                updatedNotif.Type,
                                updatedNotif.ProductionYear,
                                updatedNotif.SeriesName,
                                updatedNotif.IndexNumber,
                                updatedNotif.ParentIndexNumber);

                        _logger.LogInformation(
                            "NotifySync Upgrade Check: {Name} | pathChanged={PathChanged} (old={OldPath}, new={NewPath}) | sizeChanged={SizeChanged} (old={OldSize}, new={NewSize}) | dateChanged={DateChanged} | legacyFallback={Legacy}",
                            updatedNotif.Name,
                            pathChanged,
                            existing.FilePath ?? "NULL",
                            updatedNotif.FilePath ?? "NULL",
                            sizeChanged,
                            existing.Size?.ToString(CultureInfo.InvariantCulture) ?? "NULL",
                            updatedNotif.Size?.ToString(CultureInfo.InvariantCulture) ?? "NULL",
                            dateChanged,
                            legacyFallback);

                        if (pathChanged || (sizeChanged && dateChanged) || legacyFallback)
                        {
                            updatedNotif.IsUpgrade = true;
                            updatedNotif.DateCreated = DateTime.UtcNow; // Remonter en tête de liste
                        }
                        else
                        {
                            updatedNotif.DateCreated = existing.DateCreated;
                            updatedNotif.IsUpgrade = existing.IsUpgrade;
                        }

                        _notifications[existingIndex] = updatedNotif;
                        dbNeedsUpdate = true;
                        Interlocked.Increment(ref _versionCounter);
                    }
                    else
                    {
                        // No longer passes filters
                        _notifications.RemoveAt(existingIndex);
                        dbNeedsUpdate = true;
                        Interlocked.Increment(ref _versionCounter);
                    }
                }
                else
                {
                    isGhostItemRescue = true;
                }
            }
            finally
            {
                if (_dataLock.IsWriteLockHeld)
                {
                    _dataLock.ExitWriteLock();
                }
            }

            if (isGhostItemRescue)
            {
                var notifCheck = CreateNotificationFromItem(e.Item);
                if (notifCheck != null)
                {
                    _eventBuffer.Enqueue(e.Item);
                    if (!_disposeCts.IsCancellationRequested)
                    {
                        try
                        {
                            _bufferProcessTimer.Change(500, Timeout.Infinite);
                        }
                        catch (ObjectDisposedException)
                        {
                            // Timer already disposed during plugin shutdown — safe to ignore
                        }
                    }
                }
            }

            if (dbNeedsUpdate)
            {
                if (updatedNotif != null)
                {
                    _db.SaveNotifications(new[] { updatedNotif });
                }
                else
                {
                    _db.DeleteNotifications(new[] { e.Item.Id.ToString() });
                }
            }
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
                        // Primary upgrade detection: check if this item was just removed
                        // (Radarr/Sonarr upgrade flow: ItemRemoved → ItemAdded for same ID)
                        bool recentlyRemoved = _recentlyRemovedIds.TryRemove(notif.Id, out var removedAt)
                            && (DateTime.UtcNow - removedAt).TotalMinutes < 10;

                        // Fallback: check deleted history (delete + re-add with new Jellyfin ID)
                        bool deletedMatch = !recentlyRemoved
                            && _db.HasRecentDeletedMatch(
                                notif.Name,
                                notif.Type,
                                notif.ProductionYear,
                                notif.SeriesName,
                                notif.IndexNumber,
                                notif.ParentIndexNumber);

                        _logger.LogInformation(
                            "NotifySync ProcessBuffer: {Name} Type={Type} Year={Year} | recentlyRemoved={Removed} | deletedMatch={Match}",
                            notif.Name,
                            notif.Type,
                            notif.ProductionYear,
                            recentlyRemoved,
                            deletedMatch);

                        if (!notif.IsUpgrade && (recentlyRemoved || deletedMatch))
                        {
                            notif.IsUpgrade = true;
                        }

                        newItems.Add(notif);
                    }
                }

                if (newItems.Count > 0)
                {
                    var itemsToDelete = new List<string>();
                    var itemsToSave = new List<NotificationItem>(newItems);

                    try
                    {
                        _dataLock.EnterWriteLock();
                        foreach (var ni in itemsToSave)
                        {
                            _notifications.RemoveAll(n => n.Id == ni.Id);
                            _notifications.Add(ni);
                        }

                        // Apply Quota per category
                        var quotaResult = CategoryQuotaService.ApplyCategoryQuotas(_notifications, DatabaseCategoryLimit);
                        var finalNotifications = quotaResult.Kept;
                        itemsToDelete.AddRange(quotaResult.RemovedIds);

                        _notifications = finalNotifications.OrderByDescending(n => n.DateCreated).ToList();

                        if (_notifications.Count > GlobalRetentionLimit)
                        {
                            var overLimit = _notifications.Skip(GlobalRetentionLimit).Select(n => n.Id).ToList();
                            itemsToDelete.AddRange(overLimit!);
                            _notifications = _notifications.Take(GlobalRetentionLimit).ToList();
                        }

                        Interlocked.Increment(ref _versionCounter);
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
                _logger.LogError(ex, "Error processing notification buffer.");
            }
            finally
            {
                Interlocked.Exchange(ref _isProcessingBuffer, 0);
            }
        }

        private void PopulateInitialHistory(IProgress<double> progress, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting manual NotifySync history scan...");

            var config = Plugin.Instance?.Configuration;
            int maxItems = config?.MaxItems ?? 10;

            // diagnostic
            if (config != null)
            {
                var enabledStr = config.EnabledLibraries != null ? string.Join(", ", config.EnabledLibraries) : "null";
                var manualStr = config.ManualLibraryIds != null ? string.Join(", ", config.ManualLibraryIds) : "null";
                _logger.LogInformation("NotifySync Config: EnabledLibraries=[{Libs}], ManualLibraryIds=[{Manual}]", enabledStr, manualStr);
            }

            var validLibraryIds = new HashSet<Guid>();
            bool hasExplicit = false;

            if (config != null)
            {
                if (config.EnabledLibraries != null)
                {
                    foreach (var id in config.EnabledLibraries)
                    {
                        if (Guid.TryParse(id, out var g))
                        {
                            validLibraryIds.Add(g);
                            hasExplicit = true;
                        }
                    }
                }

                if (config.ManualLibraryIds != null)
                {
                    foreach (var manualId in config.ManualLibraryIds)
                    {
                        if (Guid.TryParse(manualId, out var g))
                        {
                            validLibraryIds.Add(g);
                            hasExplicit = true;
                        }
                        else if (!string.IsNullOrWhiteSpace(manualId) && _libraryManager != null)
                        {
                            // Try to look up by name among the library items
                            var rootChildren = _libraryManager.GetItemList(new InternalItemsQuery
                            {
                                Parent = _libraryManager.RootFolder,
                                IsFolder = true
                            });

                            foreach (var lib in rootChildren)
                            {
                                if (string.Equals(lib.Name, manualId.Trim(), StringComparison.OrdinalIgnoreCase))
                                {
                                    validLibraryIds.Add(lib.Id);
                                    hasExplicit = true;
                                }
                            }
                        }
                    }
                }

                if (!hasExplicit && config.CategoryMappings != null)
                {
                    foreach (var map in config.CategoryMappings)
                    {
                        if (Guid.TryParse(map.LibraryId, out var g))
                        {
                            validLibraryIds.Add(g);
                        }
                    }
                }
            }

            var ancestorIdsArray = validLibraryIds.Count > 0 ? validLibraryIds.ToArray() : null;

            var qMovie = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie },
                Recursive = true,
                OrderBy = new[] { (ItemSortBy.DateCreated, Jellyfin.Database.Implementations.Enums.SortOrder.Descending) },
                Limit = 1000, // Safe hard limit for initial history scan
                DtoOptions = new MediaBrowser.Controller.Dto.DtoOptions(false)
            };

            var qEpisode = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Episode },
                Recursive = true,
                OrderBy = new[] { (ItemSortBy.DateCreated, Jellyfin.Database.Implementations.Enums.SortOrder.Descending) },
                Limit = 2000, // Safe hard limit for initial history scan
                DtoOptions = new MediaBrowser.Controller.Dto.DtoOptions(false)
            };

            var qAudio = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Audio },
                Recursive = true,
                OrderBy = new[] { (ItemSortBy.DateCreated, Jellyfin.Database.Implementations.Enums.SortOrder.Descending) },
                Limit = 2000, // Safe hard limit for initial history scan
                DtoOptions = new MediaBrowser.Controller.Dto.DtoOptions(false)
            };

            var qChannel = new InternalItemsQuery
            {
                // Uniquement requêter les éléments du Channel (VOD/IPTV) sans limiter le type,
                // car le plugin cible (ex: XFusion) gère ses propres types virtuels.
                Recursive = true,
                OrderBy = new[] { (ItemSortBy.DateCreated, Jellyfin.Database.Implementations.Enums.SortOrder.Descending) },
                MediaTypes = new[] { MediaType.Video, MediaType.Audio },
                Limit = 2000, // Safe hard limit for initial history scan
                DtoOptions = new MediaBrowser.Controller.Dto.DtoOptions(false)
            };

            if (ancestorIdsArray != null && ancestorIdsArray.Length > 0)
            {
                qMovie.AncestorIds = ancestorIdsArray;
                qEpisode.AncestorIds = ancestorIdsArray;
                qAudio.AncestorIds = ancestorIdsArray;
                // Attention: Les éléments de Channel (XFusion) ignorent souvent AncestorIds.
                // Leur filtrage se fera via ChannelId dans IsItemInEnabledLibrary plus bas.
                // On peut occasionnellement forcer ChannelIds si l'API le supporte.
                qChannel.ChannelIds = ancestorIdsArray;
            }

            var queriesList = new List<InternalItemsQuery> { qMovie, qEpisode, qAudio };

            // Ne chercher dans les chaînes (VOD/Séries) que si l'utilisateur a configuré des bibliothèques actives.
            if (validLibraryIds.Count > 0 || config?.CategoryMappings?.Count > 0)
            {
                queriesList.Add(qChannel);
            }

            var queries = queriesList.ToArray();

            var items = new List<BaseItem>();
            foreach (var q in queries)
            {
                if (_libraryManager != null)
                {
                    var resultList = _libraryManager.GetItemList(q);
                    if (resultList != null)
                    {
                        items.AddRange(resultList);
                    }
                }
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

            _logger.LogInformation("NotifySync Scan: {Total} items returned by combined queries.", items.Count);

            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var typeName = item.GetType().Name;
                typeCounts[typeName] = typeCounts.GetValueOrDefault(typeName) + 1;

                var notif = CreateNotificationFromItem(item);
                if (notif != null)
                {
                    results.Add(notif);
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
                if (items.Count > 0)
                {
                    progress?.Report((double)count / items.Count * 100);
                }
            }

            // Log diagnostics
            _logger.LogInformation("NotifySync Scan Diagnostics: Types found: {Types}.", string.Join(", ", typeCounts.Select(kv => $"{kv.Key}={kv.Value}")));
            _logger.LogInformation("NotifySync Scan Diagnostics: Skipped (not in enabled library): {Skipped}, Skipped (null/error): {Null}.", skippedNotEnabled, skippedNull);

            var quotaResult = CategoryQuotaService.ApplyCategoryQuotas(results, DatabaseCategoryLimit);
            var newNotifs = quotaResult.Kept.OrderByDescending(n => n.DateCreated).ToList();
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
            if (newNotifs.Count == 0 && oldDbIds.Count > 0)
            {
                _logger.LogWarning("History scan returned 0 items (likely because the server is starting and the library is not ready yet). The existing database will not be cleared.");
                return;
            }

            _db.ReplaceAllNotifications(oldDbIds!, newNotifs);
            _db.PurgeOrphanedStates();

            try
            {
                _dataLock.EnterWriteLock();
                _notifications = newNotifs;
                Interlocked.Increment(ref _versionCounter);
                _logger.LogInformation("Scan complete. {Count} items indexed.", _notifications.Count);
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
            // Exception: Les éléments provenant de Channels (XFusion VOD) sont virtuels car ce sont des flux.
            if (item.IsVirtualItem && item.ChannelId == Guid.Empty)
            {
                return false;
            }

            var config = Plugin.Instance?.Configuration;
            if (config == null)
            {
                return false;
            }

            // If no libraries are explicitly checked AND no manual IDs
            // The user explicitly requested to only search in active libraries.
            // If neither is configured, we must not track anything (strict confinement).
            if ((config.EnabledLibraries == null || config.EnabledLibraries.Count == 0) &&
                (config.ManualLibraryIds == null || config.ManualLibraryIds.Count == 0))
            {
                // Uniquement autoriser via CategoryMappings s'ils existent et s'ils définissent des LibraryIds valides
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
                }

                return false;
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
                    if (Guid.TryParse(manualId, out var manualGuid))
                    {
                        if (owners.Contains(manualGuid))
                        {
                            return true;
                        }

                        // Support for Channels (like XFusion VOD/Series)
                        if (item.ChannelId != Guid.Empty && item.ChannelId == manualGuid)
                        {
                            return true;
                        }
                    }
                    else
                    {
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
            }

            return false;
        }

        /// <summary>
        /// Scans all monitored collections (BoxSets) for newly added items and creates notifications.
        /// Called by <see cref="CollectionScanTask"/> on a periodic schedule.
        /// </summary>
        /// <param name="progress">The progress reporter.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public void ScanCollections(IProgress<double> progress, CancellationToken cancellationToken)
        {
            var config = Plugin.Instance?.Configuration;
            if (config?.EnabledCollections == null || config.EnabledCollections.Count == 0)
            {
                return;
            }

            _logger.LogInformation("NotifySync: Starting collection scan ({Count} configured).", config.EnabledCollections.Count);

            var newNotifications = new List<NotificationItem>();
            int idx = 0;

            foreach (var collectionIdStr in config.EnabledCollections)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!Guid.TryParse(collectionIdStr, out var collectionGuid))
                {
                    continue;
                }

                var boxSet = _libraryManager.GetItemById(collectionGuid);
                if (boxSet == null)
                {
                    _logger.LogWarning("NotifySync: Collection {CollectionId} not found, skipped.", collectionIdStr);
                    continue;
                }

                // List current children of the collection
                var children = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    AncestorIds = new[] { collectionGuid },
                    Recursive = true,
                    IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Episode, BaseItemKind.Audio },
                    DtoOptions = new MediaBrowser.Controller.Dto.DtoOptions(false)
                });

                var currentIds = new HashSet<string>(children.Select(c => c.Id.ToString()), StringComparer.Ordinal);
                var snapshot = _db.GetCollectionSnapshot(collectionIdStr);

                if (snapshot.Count == 0)
                {
                    // First scan: store the baseline without creating notifications
                    _logger.LogInformation("NotifySync: First scan of collection \"{Name}\" — {Count} items recorded as baseline.", boxSet.Name, currentIds.Count);
                    _db.UpdateCollectionSnapshot(collectionIdStr, currentIds);
                }
                else
                {
                    var newIds = currentIds.Except(snapshot).ToList();
                    if (newIds.Count > 0)
                    {
                        var newIdSet = new HashSet<string>(newIds, StringComparer.Ordinal);
                        foreach (var child in children.Where(c => newIdSet.Contains(c.Id.ToString())))
                        {
                            var notif = CreateNotificationFromCollectionItem(child, boxSet.Name, collectionGuid);
                            if (notif != null)
                            {
                                newNotifications.Add(notif);
                            }
                        }

                        _logger.LogInformation("NotifySync: {Count} new items detected in collection \"{Name}\".", newIds.Count, boxSet.Name);
                    }

                    _db.UpdateCollectionSnapshot(collectionIdStr, currentIds);
                }

                idx++;
                progress?.Report((double)idx / config.EnabledCollections.Count * 100);
            }

            // Clean up snapshots for collections removed from configuration
            _db.RemoveStaleCollectionSnapshots(config.EnabledCollections);

            if (newNotifications.Count > 0)
            {
                InjectCollectionNotifications(newNotifications);
            }

            _logger.LogInformation("NotifySync: Collection scan complete. {Count} new notifications.", newNotifications.Count);
        }

        /// <summary>
        /// Injects collection-sourced notifications into the in-memory list and persists them.
        /// </summary>
        private void InjectCollectionNotifications(List<NotificationItem> newItems)
        {
            var itemsToDelete = new List<string>();
            var itemsToSave = new List<NotificationItem>(newItems);

            try
            {
                _dataLock.EnterWriteLock();
                foreach (var ni in itemsToSave)
                {
                    _notifications.RemoveAll(n => n.Id == ni.Id);
                    _notifications.Add(ni);
                }

                var quotaResult = CategoryQuotaService.ApplyCategoryQuotas(_notifications, DatabaseCategoryLimit);
                var finalNotifications = quotaResult.Kept;
                itemsToDelete.AddRange(quotaResult.RemovedIds);

                _notifications = finalNotifications.OrderByDescending(n => n.DateCreated).ToList();

                if (_notifications.Count > GlobalRetentionLimit)
                {
                    var overLimit = _notifications.Skip(GlobalRetentionLimit).Select(n => n.Id).ToList();
                    itemsToDelete.AddRange(overLimit!);
                    _notifications = _notifications.Take(GlobalRetentionLimit).ToList();
                }

                Interlocked.Increment(ref _versionCounter);
            }
            finally
            {
                if (_dataLock.IsWriteLockHeld)
                {
                    _dataLock.ExitWriteLock();
                }
            }

            _db.SaveNotifications(itemsToSave);
            if (itemsToDelete.Count > 0)
            {
                _db.DeleteNotifications(itemsToDelete);
            }
        }

        /// <summary>
        /// Creates a notification from an item found in a monitored collection.
        /// Unlike <see cref="CreateNotificationFromItem"/>, this does NOT check library membership.
        /// </summary>
        private NotificationItem? CreateNotificationFromCollectionItem(BaseItem item, string collectionName, Guid collectionId)
        {
            // Ignorer les dossiers
            if (item.IsFolder || item is Folder)
            {
                return null;
            }

            // Ignorer les Extras
            if (item.ExtraType.HasValue)
            {
                return null;
            }

            try
            {
                var notif = new NotificationItem
                {
                    Id = $"col:{collectionId:N}:{item.Id}",
                    RealItemId = item.Id.ToString(),
                    Name = item.Name ?? "Unknown",
                    Category = collectionName,
                    SeriesName = (item as Episode)?.SeriesName,
                    SeriesId = (item as Episode)?.SeriesId.ToString(),
                    DateCreated = item.DateCreated.ToUniversalTime(),
                    Type = item.GetType().Name,
                    RunTimeTicks = item.RunTimeTicks,
                    ProductionYear = item.ProductionYear,
                    BackdropImageTags = item.ImageInfos.Where(i => i.Type == ImageType.Backdrop).Select(i => i.DateModified.Ticks.ToString(CultureInfo.InvariantCulture)).ToList(),
                    PrimaryImageTag = item.ImageInfos.Where(i => i.Type == ImageType.Primary).Select(i => i.DateModified.Ticks.ToString(CultureInfo.InvariantCulture)).FirstOrDefault(),
                    IndexNumber = item.IndexNumber,
                    ParentIndexNumber = item.ParentIndexNumber,
                    DateModifiedTicks = item.DateModified.Ticks,
                    Size = item.Size,
                    FilePath = item.Path
                };

                if (item.GetBaseItemKind() == BaseItemKind.Audio && item is MediaBrowser.Controller.Entities.Audio.Audio audioItem)
                {
                    notif.SeriesName = audioItem.Album;
                    notif.SeriesId = audioItem.ParentId.ToString();
                }

                return notif;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "NotifySync: Failed to create collection notification for item {ItemName}.", item?.Name);
                return null;
            }
        }

        private NotificationItem? CreateNotificationFromItem(BaseItem item)
        {
            if (!IsItemInEnabledLibrary(item))
            {
                return null;
            }

            // Ignore folders (e.g. root VOD/Series categories from XFusion)
            if (item.IsFolder || item is Folder)
            {
                return null;
            }

            // Ignore Extras (Openings, Endings, ThemeVideos, etc.)
            if (item.ExtraType.HasValue)
            {
                return null;
            }

            // Heuristic to ignore themes and opening/ending sequences
            // Filter out short VOD items, theme songs, and misclassified openings/endings
            if (item is Episode ep && (ep.ParentIndexNumber == 0 || ep.IndexNumber == 0))
            {
                string itemName = ep.Name ?? string.Empty;
                if (itemName.Contains("opening", StringComparison.OrdinalIgnoreCase) || itemName.Contains("ending", StringComparison.OrdinalIgnoreCase) ||
                    itemName.Contains("ncop", StringComparison.OrdinalIgnoreCase) || itemName.Contains("nced", StringComparison.OrdinalIgnoreCase) ||
                    itemName.StartsWith("op ", StringComparison.OrdinalIgnoreCase) || itemName.StartsWith("ed ", StringComparison.OrdinalIgnoreCase) ||
                    itemName.Equals("op", StringComparison.OrdinalIgnoreCase) || itemName.Equals("ed", StringComparison.OrdinalIgnoreCase) ||
                    itemName.Contains("theme", StringComparison.OrdinalIgnoreCase) || itemName.Contains("thème", StringComparison.OrdinalIgnoreCase) ||
                    itemName.Contains("credit", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }

            // Exclude audio tracks used as themes for series/movies (often Theme.mp3)
            if (item is MediaBrowser.Controller.Entities.Audio.Audio && item.Name != null)
            {
                string itemName = item.Name;
                if (itemName.Equals("theme", StringComparison.OrdinalIgnoreCase) || itemName.Equals("thème", StringComparison.OrdinalIgnoreCase) || itemName.Contains("theme song", StringComparison.OrdinalIgnoreCase) || itemName.Contains("main theme", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }

            try
            {
                string category = "Other";
                var owners = item.GetAncestorIds().ToArray();
                var config = Plugin.Instance?.Configuration;

                if (config != null)
                {
                    foreach (var map in config.CategoryMappings)
                    {
                        if (Guid.TryParse(map.LibraryId, out var libGuid))
                        {
                            if (owners.Contains(libGuid))
                            {
                                category = map.CategoryName;
                                break;
                            }

                            if (item.ChannelId != Guid.Empty && item.ChannelId == libGuid)
                            {
                                category = map.CategoryName;
                                break;
                            }
                        }
                    }
                }

                var notif = new NotificationItem
                {
                    Id = item.Id.ToString(),
                    Name = item.Name ?? "Unknown",
                    Category = category,
                    SeriesName = (item as Episode)?.SeriesName,
                    SeriesId = (item as Episode)?.SeriesId.ToString(),
                    DateCreated = item.DateCreated.ToUniversalTime(),
                    Type = item.GetType().Name,
                    RunTimeTicks = item.RunTimeTicks,
                    ProductionYear = item.ProductionYear,
                    BackdropImageTags = item.ImageInfos.Where(i => i.Type == ImageType.Backdrop).Select(i => i.DateModified.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList(),
                    PrimaryImageTag = item.ImageInfos.Where(i => i.Type == ImageType.Primary).Select(i => i.DateModified.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)).FirstOrDefault(),
                    IndexNumber = item.IndexNumber,
                    ParentIndexNumber = item.ParentIndexNumber,
                    DateModifiedTicks = item.DateModified.Ticks,
                    Size = item.Size,
                    FilePath = item.Path
                };

                if (item.GetBaseItemKind() == BaseItemKind.Audio && item is MediaBrowser.Controller.Entities.Audio.Audio audioItem)
                {
                    notif.SeriesName = audioItem.Album;
                    notif.SeriesId = audioItem.ParentId.ToString();
                }

                return notif;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "NotifySync: Failed to create notification for item {ItemName}.", item?.Name);
                return null;
            }
        }
    }
}
