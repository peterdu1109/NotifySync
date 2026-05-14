using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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

        // Upgrade kind constants. Stored in NotificationItem.UpgradeKind and read by the
        // client to render a precise sub-label next to the UPD/MAJ badge.
        private const string KindQuality = "quality";
        private const string KindCodec = "codec";
        private const string KindAudio = "audio";
        private const string KindMinor = "minor";

        // Path tokens (lowercase) used to detect upgrade type from filename conventions.
        // Tokens are matched as standalone tags surrounded by non-alphanumeric separators
        // (dots, dashes, underscores, spaces) — see ContainsTag for the exact rule.
        private static readonly string[] ResolutionUpTokens4K = { "2160p", "4k", "uhd" };
        private static readonly string[] ResolutionUpTokens1080 = { "1080p" };
        private static readonly string[] SourceBetterTokens = { "bluray", "blu-ray", "remux", "blueray" };
        private static readonly string[] CodecNewTokens = { "hevc", "x265", "h265", "h.265", "av1" };
        private static readonly string[] DubbedTokens = { "vff", "vfq", "vfi", "vf", "truefrench", "french", "multi", "dubbed", "dub" };

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

        /// <summary>
        /// Classifies the type of upgrade detected when replacing an existing notification's
        /// media file. Returns one of the <c>Kind*</c> constants or <c>null</c> if no pattern
        /// matched (the client will then render just "MAJ"/"UPD" without a sub-label).
        /// </summary>
        private static string? ClassifyUpgrade(NotificationItem existing, NotificationItem updated)
        {
            string oldPath = (existing.FilePath ?? string.Empty).ToLowerInvariant();
            string newPath = (updated.FilePath ?? string.Empty).ToLowerInvariant();
            long oldSize = existing.Size ?? 0;
            long newSize = updated.Size ?? 0;
            bool pathChanged = !string.IsNullOrEmpty(oldPath) && oldPath != newPath;

            // 1. Quality — significant size jump, or resolution/source upgrade in filename
            if (oldSize > 0 && newSize > (long)(oldSize * 1.5))
            {
                return KindQuality;
            }

            if (pathChanged)
            {
                bool oldHas4K = ContainsAnyTag(oldPath, ResolutionUpTokens4K);
                bool newHas4K = ContainsAnyTag(newPath, ResolutionUpTokens4K);
                bool oldHas1080 = ContainsAnyTag(oldPath, ResolutionUpTokens1080);
                bool newHas1080 = ContainsAnyTag(newPath, ResolutionUpTokens1080);
                bool oldHasBetterSource = ContainsAnyTag(oldPath, SourceBetterTokens);
                bool newHasBetterSource = ContainsAnyTag(newPath, SourceBetterTokens);

                // Resolution went up (no→4K, or no→1080 while not already 4K)
                if ((!oldHas4K && newHas4K) || (!oldHas1080 && newHas1080 && !oldHas4K))
                {
                    return KindQuality;
                }

                // Source went up (WEB/HDTV → BluRay/REMUX)
                if (!oldHasBetterSource && newHasBetterSource)
                {
                    return KindQuality;
                }

                // 2. Codec — new codec marker (HEVC/x265/AV1) and size is not bigger
                bool oldHasNewCodec = ContainsAnyTag(oldPath, CodecNewTokens);
                bool newHasNewCodec = ContainsAnyTag(newPath, CodecNewTokens);
                if (!oldHasNewCodec && newHasNewCodec && (oldSize == 0 || newSize <= oldSize * 1.1))
                {
                    return KindCodec;
                }

                // 3. Audio — went from subtitled-only to dubbed, or got MULTI track
                bool oldHasDub = ContainsAnyTag(oldPath, DubbedTokens);
                bool newHasDub = ContainsAnyTag(newPath, DubbedTokens);
                if (!oldHasDub && newHasDub)
                {
                    return KindAudio;
                }
            }

            // 4. Minor — same path, small delta, no signal otherwise
            //    (typically: external subtitle file added, metadata refresh that touched the file,
            //     small audio track re-mux without rename)
            long sizeDelta = Math.Abs(newSize - oldSize);
            if (!pathChanged && sizeDelta < 50_000_000L)
            {
                return KindMinor;
            }

            // 5. No pattern matched — let the client render plain "MAJ"/"UPD"
            return null;
        }

        /// <summary>
        /// Returns true if <paramref name="path"/> contains any of the given <paramref name="tags"/>
        /// as a standalone token (delimited by start/end of string or non-alphanumeric separators).
        /// Prevents false matches like "vf" inside "movie name vfx" (extras).
        /// </summary>
        private static bool ContainsAnyTag(string path, string[] tags)
        {
            foreach (var tag in tags)
            {
                if (ContainsTag(path, tag))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Single-tag word-boundary match (case-insensitive — caller normalizes to lowercase).
        /// </summary>
        private static bool ContainsTag(string path, string tag)
        {
            string pattern = $"(?:^|[^a-z0-9]){Regex.Escape(tag)}(?:$|[^a-z0-9])";
            return Regex.IsMatch(path, pattern, RegexOptions.CultureInvariant);
        }

        /// <summary>
        /// Wraps <see cref="NotificationDatabase.HasRecentDeletedMatch"/> with a fast short-circuit
        /// when DeletedItems tracking is disabled — the table is empty/stale and the SQL query is wasted.
        /// </summary>
        private bool TryDeletedMatch(string name, string type, int? year, string? seriesName, int? indexNumber, int? parentIndexNumber)
        {
            if (Plugin.Instance?.Configuration?.EnableDeletedTracking != true)
            {
                return false;
            }

            return _db.HasRecentDeletedMatch(name, type, year, seriesName, indexNumber, parentIndexNumber);
        }

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

            // Note: we intentionally do NOT pre-filter by IsItemInEnabledLibrary here.
            // At ItemAdded time, the item's parent chain (GetAncestorIds) may not yet be
            // fully resolved — the library root can be missing from the ancestor list,
            // causing valid items to be rejected and never reach ProcessBuffer.
            // The library check is performed downstream in CreateNotificationFromItem
            // where the ancestry is reliable, even if the cost is one extra dequeue.
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

            // Log deleted item if tracking is enabled.
            // Note: we intentionally do NOT filter by IsItemInEnabledLibrary here — at remove time
            // the item is already detached from its library hierarchy, so GetAncestorIds() may
            // return an incomplete list. Filtering at this point breaks upgrade detection for the
            // delete+re-import scenario (scenario 2) where the new file lands with a different ID.
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
        }

        private void OnItemUpdated(object? sender, ItemChangeEventArgs e)
        {
            if (e.Item == null || (e.Item.GetType().Name != "Movie" && e.Item.GetType().Name != "Episode" && e.Item.GetType().Name != "Audio"))
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
                            && TryDeletedMatch(
                                updatedNotif.Name,
                                updatedNotif.Type,
                                updatedNotif.ProductionYear,
                                updatedNotif.SeriesName,
                                updatedNotif.IndexNumber,
                                updatedNotif.ParentIndexNumber);

                        _logger.LogDebug(
                            "NotifySync Upgrade Check: {Name} | pathChanged={PathChanged} | sizeChanged={SizeChanged} | dateChanged={DateChanged} | legacyFallback={Legacy}",
                            updatedNotif.Name,
                            pathChanged,
                            sizeChanged,
                            dateChanged,
                            legacyFallback);

                        if (pathChanged || (sizeChanged && dateChanged) || legacyFallback)
                        {
                            updatedNotif.IsUpgrade = true;
                            updatedNotif.UpgradeKind = ClassifyUpgrade(existing, updatedNotif);
                            updatedNotif.DateCreated = DateTime.UtcNow; // Remonter en tête de liste
                            _logger.LogInformation(
                                "NotifySync Upgrade Detected: {Name} | kind={Kind} | pathChanged={PathChanged} (old={OldPath}, new={NewPath}) | sizeChanged={SizeChanged}",
                                updatedNotif.Name,
                                updatedNotif.UpgradeKind ?? "unspecified",
                                pathChanged,
                                existing.FilePath ?? "NULL",
                                updatedNotif.FilePath ?? "NULL",
                                sizeChanged);
                        }
                        else if (!existing.IsUpgrade
                            && TryDeletedMatch(
                                updatedNotif.Name,
                                updatedNotif.Type,
                                updatedNotif.ProductionYear,
                                updatedNotif.SeriesName,
                                updatedNotif.IndexNumber,
                                updatedNotif.ParentIndexNumber))
                        {
                            // Metadata now available — re-check deleted history for upgrade detection.
                            // ProcessBuffer may have missed it due to null SeriesName at ItemAdded time.
                            updatedNotif.IsUpgrade = true;
                            updatedNotif.UpgradeKind = ClassifyUpgrade(existing, updatedNotif);
                            updatedNotif.DateCreated = DateTime.UtcNow;
                            _logger.LogInformation(
                                "NotifySync Upgrade Check: {Name} | kind={Kind} | deletedMatch=True (detected on metadata refresh)",
                                updatedNotif.Name,
                                updatedNotif.UpgradeKind ?? "unspecified");
                        }
                        else
                        {
                            updatedNotif.DateCreated = existing.DateCreated;
                            updatedNotif.IsUpgrade = existing.IsUpgrade;
                            updatedNotif.UpgradeKind = existing.UpgradeKind;
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
                        // Skip items that already exist in notifications (library rescan noise).
                        // OnItemUpdated handles upgrade detection for existing items via
                        // HasRecentDeletedMatch, so we can skip the DB query here entirely.
                        bool alreadyExists = false;
                        try
                        {
                            _dataLock.EnterReadLock();
                            alreadyExists = _notifications.Any(n => n.Id == notif.Id);
                        }
                        finally
                        {
                            if (_dataLock.IsReadLockHeld)
                            {
                                _dataLock.ExitReadLock();
                            }
                        }

                        if (alreadyExists)
                        {
                            _logger.LogDebug(
                                "NotifySync ProcessBuffer: skipping existing item {Name}",
                                notif.Name);
                            continue;
                        }

                        // New item — check deleted history for upgrade detection.
                        bool deletedMatch = TryDeletedMatch(
                            notif.Name,
                            notif.Type,
                            notif.ProductionYear,
                            notif.SeriesName,
                            notif.IndexNumber,
                            notif.ParentIndexNumber);

                        _logger.LogDebug(
                            "NotifySync ProcessBuffer: {Name} Type={Type} Year={Year} | deletedMatch={Match}",
                            notif.Name,
                            notif.Type,
                            notif.ProductionYear,
                            deletedMatch);

                        if (!notif.IsUpgrade && deletedMatch)
                        {
                            notif.IsUpgrade = true;
                            _logger.LogInformation(
                                "NotifySync Upgrade Detected (ProcessBuffer): {Name} | deletedMatch=True",
                                notif.Name);
                        }

                        newItems.Add(notif);
                    }
                }

                if (newItems.Count > 0)
                {
                    MergeAndPersistNotifications(newItems);
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

        /// <summary>
        /// Merges new notifications into the in-memory list, applies quotas, and persists to DB.
        /// </summary>
        private void MergeAndPersistNotifications(List<NotificationItem> newItems)
        {
            var itemsToDelete = new List<string>();

            try
            {
                _dataLock.EnterWriteLock();
                foreach (var ni in newItems)
                {
                    _notifications.RemoveAll(n => n.Id == ni.Id);
                    _notifications.Add(ni);
                }

                var quotaResult = CategoryQuotaService.ApplyCategoryQuotas(_notifications, DatabaseCategoryLimit);
                itemsToDelete.AddRange(quotaResult.RemovedIds);

                _notifications = quotaResult.Kept.OrderByDescending(n => n.DateCreated).ToList();

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

            _db.SaveNotifications(newItems);
            if (itemsToDelete.Count > 0)
            {
                _db.DeleteNotifications(itemsToDelete);
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
                MergeAndPersistNotifications(newNotifications);
            }

            _logger.LogInformation("NotifySync: Collection scan complete. {Count} new notifications.", newNotifications.Count);
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
