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

        /// <summary>
        /// How long a removed folder stays a candidate for move detection.
        /// <para>
        /// It only covers the order where the source library is scanned FIRST — the folder has
        /// to still be on record when the episodes reappear. Measured on a live server those two
        /// moments were six seconds apart, the same scan, so five minutes is fifty times the
        /// observed gap. The opposite order needs no window at all: the removal-side mirror
        /// matches against notifications still in the bell, however long afterwards it arrives.
        /// </para>
        /// <para>
        /// Deliberately short, because the two failure modes are not equally bad. Missing a move
        /// shows one card too many — visible, and it ages out. Overreaching swallows a genuine
        /// addition in silence, which is the one thing a notification plugin must never do: a
        /// series deleted and re-downloaded elsewhere would go unannounced. Five minutes is far
        /// too short for that to happen by accident.
        /// </para>
        /// </summary>
        private const int MovedFolderWindowMinutes = 5;

        // Upgrade kind constants. Stored in NotificationItem.UpgradeKind and read by the
        // client to render a precise sub-label next to the UPD/MAJ badge.
        // We deliberately surface only three kinds; anything that doesn't match one of
        // these signals is treated as a non-upgrade (item stays in place, no badge).
        private const string KindQuality = "quality";
        private const string KindCodec = "codec";
        private const string KindAudio = "audio";

        // Path tokens (lowercase) used to detect upgrade type from filename conventions.
        // Tokens are matched as standalone tags surrounded by non-alphanumeric separators
        // (dots, dashes, underscores, spaces) — see ContainsTag for the exact rule.
        private static readonly string[] ResolutionUpTokens4K = { "2160p", "4k", "uhd" };
        private static readonly string[] ResolutionUpTokens1080 = { "1080p" };
        private static readonly string[] SourceBetterTokens = { "bluray", "blu-ray", "remux", "blueray" };

        // Audio-track-added tokens: when any of these appears in the new filename but not
        // in the old one, we flag MAJ • Audio. Covers FR variants (priority for the French
        // audience), multi-language markers, and generic "dubbed" markers. VOSTFR is NOT
        // here — it's subtitles-only, not an audio change.
        private static readonly string[] AudioAddedTokens =
        {
            "vff", "vfq", "vfi", "vf", "truefrench", "french",
            "multi", "dual",
            "dubbed", "dub"
        };

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
        /// Returns null for an unusable item reference — an empty/all-zeros GUID.
        /// During a library scan Jellyfin creates episodes before linking them to their
        /// series, so <c>Episode.SeriesId</c> can still be <see cref="Guid.Empty"/>;
        /// <c>ToString()</c> then yields "00000000-0000-0000-0000-000000000000", a
        /// NON-empty string that passes IsNullOrEmpty checks and reaches
        /// <c>GetItemById</c>, which rejects an empty GUID with ArgumentException.
        /// Storing it also made every such episode share one bogus series group.
        /// </summary>
        private static string? UsableIdOrNull(string? id)
            => (!string.IsNullOrEmpty(id) && Guid.TryParse(id, out var g) && g != Guid.Empty) ? id : null;

        /// <summary>
        /// Classifies the type(s) of upgrade detected when replacing an existing notification's
        /// media file. Returns a comma-separated list of kinds (e.g. <c>"codec,audio"</c> or
        /// <c>"quality,codec,audio"</c>) in display priority order, or <c>null</c> when no
        /// meaningful change is detected. The client splits this and renders each kind as a
        /// localized label (e.g. "MAJ Codec + Audio"). Detection is purely filename-based —
        /// release-group naming conventions are the only reliable signal here. Size, bitrate,
        /// container, and pixel-dimension heuristics were tried (Phase B Lite) and produced
        /// too many false positives.
        /// </summary>
        private static string? ClassifyUpgrade(NotificationItem existing, NotificationItem updated)
        {
            string oldPath = (existing.FilePath ?? string.Empty).ToLowerInvariant();
            string newPath = (updated.FilePath ?? string.Empty).ToLowerInvariant();

            // No path-based signal possible without a usable old path. The caller will
            // treat this as "unspecified MAJ" or, on the deleted-match path, decide to
            // skip the upgrade flag entirely.
            if (string.IsNullOrEmpty(oldPath) || oldPath == newPath)
            {
                return null;
            }

            // Size suppressor: when both file sizes are known and byte-for-byte identical,
            // this is the same file under a different name (a manual rename/move), not a
            // real replacement — suppress even if the filename tokens differ. An exact
            // size match never occurs for a genuine re-encode (which always changes size).
            // Only applies when both sizes exist; otherwise we fall back to token logic.
            if (existing.Size.HasValue && updated.Size.HasValue && existing.Size.Value == updated.Size.Value)
            {
                return null;
            }

            var kinds = new List<string>(3);

            // 1. Quality — resolution or source token differs (in either direction).
            //    Symmetric: 1080p → 4K and 4K → 1080p both qualify. Same for source
            //    (WEBRip ↔ BluRay).
            bool oldHas4K = ContainsAnyTag(oldPath, ResolutionUpTokens4K);
            bool newHas4K = ContainsAnyTag(newPath, ResolutionUpTokens4K);
            bool oldHas1080 = ContainsAnyTag(oldPath, ResolutionUpTokens1080);
            bool newHas1080 = ContainsAnyTag(newPath, ResolutionUpTokens1080);
            bool oldHasBetterSource = ContainsAnyTag(oldPath, SourceBetterTokens);
            bool newHasBetterSource = ContainsAnyTag(newPath, SourceBetterTokens);

            if (oldHas4K != newHas4K || oldHas1080 != newHas1080 || oldHasBetterSource != newHasBetterSource)
            {
                kinds.Add(KindQuality);
            }

            // 2. Codec — codec family changed in either direction (x264 ↔ HEVC ↔ AV1).
            string? oldCodec = DetectCodec(oldPath);
            string? newCodec = DetectCodec(newPath);
            if (oldCodec != newCodec && (oldCodec != null || newCodec != null))
            {
                kinds.Add(KindCodec);
            }

            // 3. Audio — any audio-track-added token appears in the new filename but not
            //    in the old one (asymmetric on purpose — removing a track isn't an upgrade).
            foreach (var token in AudioAddedTokens)
            {
                if (!ContainsTag(oldPath, token) && ContainsTag(newPath, token))
                {
                    kinds.Add(KindAudio);
                    break;
                }
            }

            return kinds.Count == 0 ? null : string.Join(",", kinds);
        }

        /// <summary>
        /// Extracts the dominant video codec family from a filename path.
        /// Returns "av1", "hevc", "x264", or null if no codec marker is present.
        /// Used by <see cref="ClassifyUpgrade"/> to detect codec transitions in any direction.
        /// </summary>
        private static string? DetectCodec(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            if (ContainsTag(path, "av1"))
            {
                return "av1";
            }

            if (ContainsTag(path, "hevc") || ContainsTag(path, "x265") || ContainsTag(path, "h265") || ContainsTag(path, "h.265"))
            {
                return "hevc";
            }

            if (ContainsTag(path, "x264") || ContainsTag(path, "h264") || ContainsTag(path, "h.264") || ContainsTag(path, "avc"))
            {
                return "x264";
            }

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
        /// Returns the most recent matching deleted record (or null) when DeletedItems
        /// tracking is enabled. Used by both <see cref="ProcessBuffer"/> (new-item path)
        /// and <see cref="OnItemUpdated"/> (metadata-refresh recovery) so ClassifyUpgrade
        /// can compare the new file against the path of the file that was just deleted.
        /// </summary>
        /// <summary>
        /// True when the new file sits under a folder that was removed from somewhere else a
        /// moment ago — i.e. it did not arrive, it moved.
        /// <para>
        /// Jellyfin identifies items by path, so relocating a series produces a removal for the
        /// series FOLDER (never for its episodes) and then a fresh add per episode, with no
        /// series name and no season/episode numbers yet. Nothing in that pair can be matched on
        /// metadata. What does survive the move is the folder's own name, present on both sides.
        /// </para>
        /// </summary>
        /// <param name="filePath">The new file's path.</param>
        /// <param name="deletedFolders">Folder paths removed recently, fetched once per batch.</param>
        /// <param name="matched">The folder that matched, for logging.</param>
        /// <returns><c>true</c> when this add is the tail of a move.</returns>
        private static bool CameFromDeletedFolder(string? filePath, IReadOnlyList<string> deletedFolders, out string matched)
        {
            matched = string.Empty;
            if (string.IsNullOrEmpty(filePath) || deletedFolders.Count == 0)
            {
                return false;
            }

            var segments = filePath.Split('/', '\\');

            // The last segment is the file itself; only its ancestors can be the moved folder.
            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (string.IsNullOrEmpty(segments[i]))
                {
                    continue;
                }

                foreach (var folder in deletedFolders)
                {
                    string trimmed = folder.TrimEnd('/', '\\');
                    string folderName = trimmed.Split('/', '\\').LastOrDefault() ?? string.Empty;
                    if (folderName.Length == 0 || !string.Equals(folderName, segments[i], StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Same name rebuilt at the same absolute location means the series never
                    // went anywhere — a refresh that dropped and re-created it in place.
                    string rebuilt = string.Join('/', segments.Take(i + 1));
                    if (string.Equals(rebuilt, trimmed.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    matched = folder;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when two copies of a title are byte-for-byte the same size — the signature of
        /// a file that was moved or renamed rather than replaced. A genuine re-encode always
        /// changes the size, so this never masks a real upgrade.
        /// </summary>
        /// <param name="deletedSize">Size of the copy that disappeared.</param>
        /// <param name="addedSize">Size of the copy that appeared.</param>
        /// <returns><c>true</c> when both sizes are known and identical.</returns>
        private static bool IsSameFileBackAgain(long? deletedSize, long? addedSize)
            => deletedSize.HasValue && addedSize.HasValue && deletedSize.Value > 0 && deletedSize.Value == addedSize.Value;

        /// <summary>
        /// True when an existing notification describes the same title as the item being
        /// removed, under a different id and with an identical size — i.e. the file has
        /// already been re-added somewhere else and this removal is the tail of a move.
        /// </summary>
        /// <param name="candidate">An existing notification.</param>
        /// <param name="removed">The item Jellyfin is removing.</param>
        /// <returns><c>true</c> when the notification is the twin of the removed item.</returns>
        private static bool IsSameFileReadded(NotificationItem candidate, BaseItem removed)
        {
            if (!IsSameFileBackAgain(removed.Size, candidate.Size))
            {
                return false;
            }

            string removedType = removed.GetType().Name;
            if (!string.Equals(candidate.Type, removedType, StringComparison.Ordinal))
            {
                return false;
            }

            // Episodes are identified by their slot in the series, not by their title:
            // the same episode can carry a different name across libraries (TBA, VO/VF).
            if (removed is Episode episode)
            {
                return !string.IsNullOrEmpty(candidate.SeriesName)
                    && string.Equals(candidate.SeriesName, episode.SeriesName, StringComparison.OrdinalIgnoreCase)
                    && candidate.IndexNumber == removed.IndexNumber
                    && candidate.ParentIndexNumber == removed.ParentIndexNumber;
            }

            return !string.IsNullOrEmpty(removed.Name)
                && string.Equals(candidate.Name, removed.Name, StringComparison.OrdinalIgnoreCase)
                && candidate.ProductionYear == removed.ProductionYear;
        }

        private DeletedItemRecord? TryGetDeletedMatchRecord(string name, string type, int? year, string? seriesName, int? indexNumber, int? parentIndexNumber)
        {
            if (Plugin.Instance?.Configuration?.EnableDeletedTracking != true)
            {
                return null;
            }

            return _db.TryGetDeletedMatch(name, type, year, seriesName, indexNumber, parentIndexNumber);
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

            // Unsubscribe from library events FIRST: a late ItemUpdated/ItemRemoved
            // firing after the lock/db below are disposed would hit disposed objects
            // (EnterWriteLock on a disposed ReaderWriterLockSlim throws).
            _libraryManager.ItemAdded -= OnItemAdded;
            _libraryManager.ItemRemoved -= OnItemRemoved;
            _libraryManager.ItemUpdated -= OnItemUpdated;
            _userDataManager.UserDataSaved -= OnUserDataSaved;

            _disposeCts.Cancel();
            SaveUserCleared();
            _bufferProcessTimer?.Dispose();
            _dataLock?.Dispose();
            _disposeCts.Dispose();
            _db?.Dispose();

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
            // A played/unplayed/favorite change only affects THIS user's view of the
            // bell (played items drop out, favorites light up). Bump only this user's
            // state version — NOT the global counter — so we don't invalidate every
            // other user's cached view and force a server-wide N+1 recompute on each
            // play event. The global counter stays reserved for real library changes
            // (add/remove/update) that genuinely affect everyone.
            var normalizedUserId = NormalizeUserId(e.UserId.ToString("N"));
            IncrementUserStateVersion(normalizedUserId);
            NotifyController.InvalidateUserCache(normalizedUserId);
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

            // DIAGNOSTIC BUILD ONLY — logs every removal Jellyfin hands us, before any filter.
            // On a live server, moving a library folder produced no deletion record at all while
            // deleting one episode by hand produced one instantly, and nothing in this code
            // explains the difference. This says what actually arrives.
            _logger.LogWarning(
                "NotifySync DIAG Removal: name={Name} | type={Type} | isFolder={IsFolder} | series={Series} | S{Season}E{Episode} | size={Size} | path={Path}",
                e.Item.Name ?? "NULL",
                e.Item.GetType().Name,
                e.Item.IsFolder,
                (e.Item as Episode)?.SeriesName ?? "NULL",
                e.Item.ParentIndexNumber,
                e.Item.IndexNumber,
                e.Item.Size,
                e.Item.Path ?? "NULL");

            // Live TV recordings/programs cycle naturally (recording ends, viewer dismisses, etc.)
            // and they don't carry a meaningful file path nor any "upgrade" semantics — tracking
            // their deletions just pollutes the admin Deletions tab without serving any purpose.
            if (e.Item.GetType().Name == "LiveTvProgram")
            {
                return;
            }

            // Log deleted item if tracking is enabled.
            // Note: we intentionally do NOT filter by IsItemInEnabledLibrary here — at remove time
            // the item is already detached from its library hierarchy, so GetAncestorIds() may
            // return an incomplete list. Filtering at this point breaks upgrade detection for the
            // delete+re-import scenario (scenario 2) where the new file lands with a different ID.
            var config = Plugin.Instance?.Configuration;

            // Folders used to be skipped outright. They cannot be: when a series is moved to
            // another library, Jellyfin announces the SERIES FOLDER and never its episodes —
            // measured on a live server, one removal for the folder, none for the four episodes
            // that then arrived as adds. The folder's own path is the only trace left of where
            // those files came from, and its last segment survives the move unchanged.
            // Folders without a path (Jellyfin also emits phantom "Saison inconnue" seasons)
            // carry nothing usable, so they stay out.
            bool isFolderItem = e.Item.IsFolder || e.Item is Folder;
            if (config != null && config.EnableDeletedTracking && (!isFolderItem || !string.IsNullOrEmpty(e.Item.Path)))
            {
                try
                {
                    var item = e.Item;
                    string type = item.GetType().Name;
                    string? seriesName = (item as Episode)?.SeriesName;
                    int? year = item.ProductionYear;
                    int? indexNum = item.IndexNumber;
                    int? parentIndexNum = item.ParentIndexNumber;
                    string? filePath = item.Path;
                    long? size = item.Size;

                    _db.SaveDeletedItem(item.Id.ToString(), item.Name ?? "Unknown", type, seriesName, year, indexNum, parentIndexNum, filePath, size);

                    // DIAGNOSTIC BUILD ONLY — confirms the row was written and with what.
                    _logger.LogWarning(
                        "NotifySync DIAG Tracked: name={Name} | type={Type} | series={Series} | S{Season}E{Episode} | size={Size}",
                        item.Name ?? "NULL",
                        type,
                        seriesName ?? "NULL",
                        parentIndexNum,
                        indexNum,
                        size);

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
            else
            {
                // DIAGNOSTIC BUILD ONLY — says which filter dropped this removal.
                _logger.LogWarning(
                    "NotifySync DIAG Skipped: name={Name} | configNull={NoConfig} | tracking={Tracking} | isFolder={IsFolder} | isFolderType={IsFolderType}",
                    e.Item.Name ?? "NULL",
                    config == null,
                    config?.EnableDeletedTracking,
                    e.Item.IsFolder,
                    e.Item is Folder);
            }

            var itemId = e.Item.Id.ToString();
            var removedIds = new List<string>();
            try
            {
                _dataLock.EnterWriteLock();

                // Match both the direct notification (n.Id) and synthetic collection
                // notifications (Id = "col:{collectionId}:{itemId}", RealItemId = itemId).
                // Without the RealItemId check, collection entries for a deleted media
                // linger as zombies: GetData skips them (item unresolvable) but they
                // still consume category-quota slots in memory and in the DB.
                // Mirror of the move guard in ProcessBuffer. Which of the two fires depends
                // on the order Jellyfin happens to scan the source and destination libraries
                // in: if the add lands first, the notification already exists by the time the
                // deletion reaches us, and only this side can catch it. Without both, the
                // guard would work about one migration in two.
                bool trackMoves = config != null && config.EnableDeletedTracking;

                // A folder leaving is the only announcement Jellyfin makes when a series is
                // relocated. If its episodes were re-added first, their notifications already
                // exist and this is the last chance to recognise them.
                var goneFolder = trackMoves && isFolderItem && !string.IsNullOrEmpty(e.Item.Path)
                    ? new[] { e.Item.Path! }
                    : Array.Empty<string>();

                foreach (var n in _notifications)
                {
                    if (n.Id == itemId || n.RealItemId == itemId)
                    {
                        removedIds.Add(n.Id);
                    }
                    else if (trackMoves && IsSameFileReadded(n, e.Item))
                    {
                        _logger.LogInformation(
                            "NotifySync Move Detected: {Name} | already re-added elsewhere with an identical size ({Size} bytes) — dropping its notification.",
                            n.Name,
                            n.Size);
                        removedIds.Add(n.Id);
                    }
                    else if (!n.IsUpgrade && CameFromDeletedFolder(n.FilePath, goneFolder, out var leftFolder))
                    {
                        _logger.LogWarning(
                            "NotifySync Move Detected: {Name} — already re-added elsewhere before \"{From}\" was reported gone; dropping its notification.",
                            n.Name,
                            leftFolder);
                        removedIds.Add(n.Id);
                    }
                }

                if (removedIds.Count > 0)
                {
                    var doomed = new HashSet<string>(removedIds, StringComparer.Ordinal);
                    _notifications.RemoveAll(n => doomed.Contains(n.Id));
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

            if (removedIds.Count > 0)
            {
                _db.DeleteNotifications(removedIds);
                foreach (var removedId in removedIds)
                {
                    _db.DeleteStatesForNotification(removedId);
                }
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
            bool movedElsewhere = false;

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
                        // Path change is the ONLY upgrade signal we trust. Size, date,
                        // bitrate, container variations without a path change are too
                        // noisy (Sonarr atomic moves, metadata refreshes, transcoder
                        // pipelines all touch them without a meaningful media change).
                        bool pathChanged = !string.IsNullOrEmpty(existing.FilePath)
                            && !string.IsNullOrEmpty(updatedNotif.FilePath)
                            && !string.Equals(existing.FilePath, updatedNotif.FilePath, StringComparison.Ordinal);

                        _logger.LogDebug(
                            "NotifySync Upgrade Check: {Name} | pathChanged={PathChanged}",
                            updatedNotif.Name,
                            pathChanged);

                        if (pathChanged)
                        {
                            var kind = ClassifyUpgrade(existing, updatedNotif);
                            if (kind != null)
                            {
                                updatedNotif.IsUpgrade = true;
                                updatedNotif.UpgradeKind = kind;
                                updatedNotif.DateCreated = DateTime.UtcNow; // remonter en tête de liste
                                _logger.LogInformation(
                                    "NotifySync Upgrade Detected: {Name} | kind={Kind} | old={OldPath} new={NewPath}",
                                    updatedNotif.Name,
                                    kind,
                                    existing.FilePath ?? "NULL",
                                    updatedNotif.FilePath ?? "NULL");
                            }
                            else
                            {
                                // Path changed but no Quality/Codec/Audio token signal —
                                // probably a file move or rename without a real upgrade.
                                // Keep the item where it is, no badge change.
                                updatedNotif.DateCreated = existing.DateCreated;
                                updatedNotif.IsUpgrade = existing.IsUpgrade;
                                updatedNotif.UpgradeKind = existing.UpgradeKind;
                                _logger.LogDebug(
                                    "NotifySync Upgrade Skipped: {Name} | path changed but no token signal",
                                    updatedNotif.Name);
                            }
                        }
                        else if (!existing.IsUpgrade)
                        {
                            // ProcessBuffer may have missed a deletion match at ItemAdded
                            // time (e.g. null SeriesName before metadata was filled in).
                            // Try once more now that the metadata is complete — compare the
                            // current file against the deleted record's path, not against
                            // itself.
                            var deletedRecord = TryGetDeletedMatchRecord(
                                updatedNotif.Name,
                                updatedNotif.Type,
                                updatedNotif.ProductionYear,
                                updatedNotif.SeriesName,
                                updatedNotif.IndexNumber,
                                updatedNotif.ParentIndexNumber);
                            if (deletedRecord != null)
                            {
                                var deletedAsNotif = new NotificationItem { FilePath = deletedRecord.FilePath, Size = deletedRecord.Size };
                                var kind = ClassifyUpgrade(deletedAsNotif, updatedNotif);
                                if (kind != null)
                                {
                                    updatedNotif.IsUpgrade = true;
                                    updatedNotif.UpgradeKind = kind;
                                    updatedNotif.DateCreated = DateTime.UtcNow;
                                    _db.MarkDeletedAsMatched(deletedRecord.Id, updatedNotif.Id);
                                    _logger.LogInformation(
                                        "NotifySync Upgrade Check: {Name} | kind={Kind} | deletedMatch=True (recovered on metadata refresh)",
                                        updatedNotif.Name,
                                        kind);
                                }
                                else if (IsSameFileBackAgain(deletedRecord.Size, updatedNotif.Size))
                                {
                                    // Same recovery, for the move guard. At ItemAdded time an
                                    // episode has no SeriesName yet — Jellyfin links it to its
                                    // series afterwards — so the lookup in ProcessBuffer finds
                                    // nothing and the notification gets created as brand new.
                                    // Now that the metadata is complete the deletion matches,
                                    // and an identical size means the file merely moved.
                                    movedElsewhere = true;
                                    _logger.LogInformation(
                                        "NotifySync Move Detected (recovered on metadata refresh): {Name} | identical size ({Size} bytes) as a recently deleted copy — dropping its notification.",
                                        updatedNotif.Name,
                                        updatedNotif.Size);
                                }
                                else
                                {
                                    updatedNotif.DateCreated = existing.DateCreated;
                                    updatedNotif.IsUpgrade = existing.IsUpgrade;
                                    updatedNotif.UpgradeKind = existing.UpgradeKind;
                                }
                            }
                            else
                            {
                                updatedNotif.DateCreated = existing.DateCreated;
                                updatedNotif.IsUpgrade = existing.IsUpgrade;
                                updatedNotif.UpgradeKind = existing.UpgradeKind;
                            }
                        }
                        else
                        {
                            updatedNotif.DateCreated = existing.DateCreated;
                            updatedNotif.IsUpgrade = existing.IsUpgrade;
                            updatedNotif.UpgradeKind = existing.UpgradeKind;
                        }

                        if (movedElsewhere)
                        {
                            // Leaving updatedNotif null makes the persistence step below
                            // delete the row instead of saving it.
                            _notifications.RemoveAt(existingIndex);
                            updatedNotif = null;
                        }
                        else
                        {
                            _notifications[existingIndex] = updatedNotif;
                        }

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
                    var goneId = e.Item.Id.ToString();
                    _db.DeleteNotifications(new[] { goneId });

                    // Drop the per-user read/dismissed rows too, otherwise a stale "already
                    // read" state would silently apply if this id ever came back.
                    _db.DeleteStatesForNotification(goneId);
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

                // Fetched once for the whole batch: a scan can hand us hundreds of adds and
                // this list changes only when a folder disappears.
                var deletedFolders = Plugin.Instance?.Configuration?.EnableDeletedTracking == true
                    ? _db.GetRecentlyDeletedFolderPaths(MovedFolderWindowMinutes)
                    : Array.Empty<string>();

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
                        // OnItemUpdated handles upgrade detection for existing items, so we
                        // can skip the DB query here entirely.
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
                        // We fetch the actual record (not just a bool) so ClassifyUpgrade
                        // can compare the new filename against the deleted file's filename.
                        var deletedRecord = TryGetDeletedMatchRecord(
                            notif.Name,
                            notif.Type,
                            notif.ProductionYear,
                            notif.SeriesName,
                            notif.IndexNumber,
                            notif.ParentIndexNumber);

                        // DIAGNOSTIC BUILD ONLY — raised from Debug so it shows at the default
                        // log level, next to the removal lines it needs to be read against.
                        _logger.LogWarning(
                            "NotifySync DIAG Add: {Name} | type={Type} | series={Series} | S{Season}E{Episode} | size={Size} | deletedMatch={Match}",
                            notif.Name,
                            notif.Type,
                            notif.SeriesName ?? "NULL",
                            notif.ParentIndexNumber,
                            notif.IndexNumber,
                            notif.Size,
                            deletedRecord != null);

                        // Folder rule LAST, and only when no deleted file matched. A file that
                        // replaced another is a replacement, whatever folder it landed in — and
                        // a real quality upgrade dropped into a series moved less than six hours
                        // ago would otherwise be swallowed as if it were part of the move.
                        if (deletedRecord == null && CameFromDeletedFolder(notif.FilePath, deletedFolders, out var movedFrom))
                        {
                            _logger.LogWarning(
                                "NotifySync Move Detected: {Name} — its folder left \"{From}\" moments ago; no notification created.",
                                notif.Name,
                                movedFrom);
                            continue;
                        }

                        if (!notif.IsUpgrade && deletedRecord != null)
                        {
                            var deletedAsNotif = new NotificationItem { FilePath = deletedRecord.FilePath, Size = deletedRecord.Size };
                            var kind = ClassifyUpgrade(deletedAsNotif, notif);
                            if (kind != null)
                            {
                                notif.IsUpgrade = true;
                                notif.UpgradeKind = kind;
                                _db.MarkDeletedAsMatched(deletedRecord.Id, notif.Id);
                                _logger.LogInformation(
                                    "NotifySync Upgrade Detected (ProcessBuffer): {Name} | kind={Kind} | oldPath={Old} | newPath={New}",
                                    notif.Name,
                                    kind,
                                    deletedRecord.FilePath ?? "NULL",
                                    notif.FilePath ?? "NULL");
                            }
                            else if (IsSameFileBackAgain(deletedRecord.Size, notif.Size))
                            {
                                // Moving a file between libraries (or between disks) makes
                                // Jellyfin delete the old item and create a new one with a new
                                // id, so it reaches us as a fresh add. A byte-for-byte identical
                                // size against a recent deletion of the same episode is the
                                // signature of that move — nothing was actually added, so
                                // notifying would push real news out of everyone's bell.
                                _logger.LogInformation(
                                    "NotifySync Move Detected: {Name} | identical size ({Size} bytes) as a recently deleted copy — no notification created.",
                                    notif.Name,
                                    notif.Size);
                                continue;
                            }
                            else
                            {
                                _logger.LogDebug(
                                    "NotifySync Upgrade Skipped (ProcessBuffer): {Name} | deleted match found but no Quality/Codec/Audio token signal",
                                    notif.Name);
                            }
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

            // Carry forward upgrade state from the current in-memory list. The scan
            // recreates items from the library, which knows nothing about past upgrade
            // detections — without this, a manual "Regenerate history" silently wipes
            // every UPD/MAJ badge. DateCreated is carried too so the upgraded item keeps
            // its bumped position — which NotifyController also compares against the last
            // play date and the last read date, so losing it would resurrect watched items
            // and re-light the counter for upgrades the user has already seen.
            var upgradesById = new Dictionary<string, NotificationItem>(StringComparer.Ordinal);
            try
            {
                _dataLock.EnterReadLock();
                foreach (var n in _notifications)
                {
                    if (n.IsUpgrade)
                    {
                        upgradesById[n.Id] = n;
                    }
                }
            }
            finally
            {
                if (_dataLock.IsReadLockHeld)
                {
                    _dataLock.ExitReadLock();
                }
            }

            if (upgradesById.Count > 0)
            {
                int carried = 0;
                foreach (var notif in results)
                {
                    if (upgradesById.TryGetValue(notif.Id, out var old))
                    {
                        notif.IsUpgrade = true;
                        notif.UpgradeKind = old.UpgradeKind;
                        notif.DateCreated = old.DateCreated;
                        carried++;
                    }
                }

                _logger.LogInformation("NotifySync Scan: {Count} UPD badge(s) carried over from the previous state.", carried);
            }

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
                    SeriesId = UsableIdOrNull((item as Episode)?.SeriesId.ToString()),
                    DateCreated = item.DateCreated.ToUniversalTime(),
                    Type = item.GetType().Name,
                    RunTimeTicks = item.RunTimeTicks,
                    ProductionYear = item.ProductionYear,
                    BackdropImageTags = item.ImageInfos.Where(i => i.Type == ImageType.Backdrop).Select(i => i.DateModified.Ticks.ToString(CultureInfo.InvariantCulture)).ToList(),
                    PrimaryImageTag = item.ImageInfos.Where(i => i.Type == ImageType.Primary).Select(i => i.DateModified.Ticks.ToString(CultureInfo.InvariantCulture)).FirstOrDefault(),
                    IndexNumber = item.IndexNumber,
                    ParentIndexNumber = item.ParentIndexNumber,
                    FilePath = item.Path,
                    Size = item.Size
                };

                if (item.GetBaseItemKind() == BaseItemKind.Audio && item is MediaBrowser.Controller.Entities.Audio.Audio audioItem)
                {
                    notif.SeriesName = audioItem.Album;
                    notif.SeriesId = UsableIdOrNull(audioItem.ParentId.ToString());
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
                    SeriesId = UsableIdOrNull((item as Episode)?.SeriesId.ToString()),
                    DateCreated = item.DateCreated.ToUniversalTime(),
                    Type = item.GetType().Name,
                    RunTimeTicks = item.RunTimeTicks,
                    ProductionYear = item.ProductionYear,
                    BackdropImageTags = item.ImageInfos.Where(i => i.Type == ImageType.Backdrop).Select(i => i.DateModified.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList(),
                    PrimaryImageTag = item.ImageInfos.Where(i => i.Type == ImageType.Primary).Select(i => i.DateModified.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)).FirstOrDefault(),
                    IndexNumber = item.IndexNumber,
                    ParentIndexNumber = item.ParentIndexNumber,
                    FilePath = item.Path,
                    Size = item.Size
                };

                if (item.GetBaseItemKind() == BaseItemKind.Audio && item is MediaBrowser.Controller.Entities.Audio.Audio audioItem)
                {
                    notif.SeriesName = audioItem.Album;
                    notif.SeriesId = UsableIdOrNull(audioItem.ParentId.ToString());
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
