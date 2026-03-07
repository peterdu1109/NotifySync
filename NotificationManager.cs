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
        private const int DatabaseCategoryLimit = 100;
        private const int GlobalRetentionLimit = 2000;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserDataManager _userDataManager;
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
        /// <param name="userDataManager">The user data manager.</param>
        public NotificationManager(ILibraryManager libraryManager, ILogger<NotificationManager> logger, IFileSystem fileSystem, IUserDataManager userDataManager)
        {
            _libraryManager = libraryManager;
            _logger = logger;
            _userDataManager = userDataManager;
            _jsonPath = Path.Combine(Plugin.Instance!.DataFolderPath, "notifications.json");

            _db = new NotificationDatabase(Plugin.Instance!.DataFolderPath, _logger);
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

            if (_userDataManager != null)
            {
                _userDataManager.UserDataSaved -= OnUserDataSaved;
            }

            GC.SuppressFinalize(this);
        }

        private void LoadAndMigrate()
        {
            var diskNotifs = _db.GetAllNotifications().ToList();
            _logger.LogInformation("NotifySync Startup: Loaded {Count} notifications from SQLite DB.", diskNotifs.Count);

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
            var quotaResult = ApplyCategoryQuotas(diskNotifs, DatabaseCategoryLimit);
            var finalNotifications = quotaResult.Kept;
            var itemsToDelete = quotaResult.RemovedIds;

            var newNotifs = finalNotifications.OrderByDescending(n => n.DateCreated).ToList();
            _logger.LogInformation("NotifySync Startup: Kept {Count} notifications after quota enforcement. ({Deleted} deleted)", newNotifs.Count, itemsToDelete.Count);

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

        private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
        {
            // Invalidate cache on ANY user data change (played, unplayed, season-level, etc.)
            // This ensures the bell updates when:
            //   - An item is marked as watched (disappears from bell)
            //   - An item is unmarked (reappears in bell)
            //   - A whole season is toggled (propagates to episodes)
            NotifyController.InvalidateUserCache(e.UserId.ToString("N"));
        }

        private void OnItemAdded(object? sender, ItemChangeEventArgs e)
        {
            if (e.Item == null || (e.Item.GetType().Name != "Movie" && e.Item.GetType().Name != "Episode" && e.Item.GetType().Name != "Audio"))
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
            if (e.Item == null)
            {
                return;
            }

            bool dbNeedsUpdate = false;
            NotificationItem? updatedNotif = null;

            try
            {
                _dataLock.EnterWriteLock();
                var existingIndex = _notifications.FindIndex(n => n.Id == e.Item.Id.ToString());
                if (existingIndex >= 0)
                {
                    updatedNotif = CreateNotificationFromItem(e.Item);
                    if (updatedNotif != null)
                    {
                        // Preserve original date
                        updatedNotif.DateCreated = _notifications[existingIndex].DateCreated;
                        _notifications[existingIndex] = updatedNotif;
                        dbNeedsUpdate = true;
                        Interlocked.Exchange(ref _versionCounter, DateTime.UtcNow.Ticks);
                    }
                    else
                    {
                        // No longer passes filters
                        _notifications.RemoveAt(existingIndex);
                        dbNeedsUpdate = true;
                        Interlocked.Exchange(ref _versionCounter, DateTime.UtcNow.Ticks);
                    }
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
                            _notifications.Insert(0, ni);
                        }

                        // Apply Quota per category
                        var quotaResult = ApplyCategoryQuotas(_notifications, DatabaseCategoryLimit);
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

            var ancestorIdsArray = validLibraryIds.Count > 0 ? validLibraryIds.Select(g => g).ToArray() : null;

            var qMovie = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie },
                Recursive = true,
                OrderBy = new[] { (ItemSortBy.DateCreated, (Jellyfin.Database.Implementations.Enums.SortOrder)1) },
                Limit = 1000, // Safe hard limit for initial history scan
                DtoOptions = new MediaBrowser.Controller.Dto.DtoOptions(false)
            };

            var qEpisode = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Episode },
                Recursive = true,
                OrderBy = new[] { (ItemSortBy.DateCreated, (Jellyfin.Database.Implementations.Enums.SortOrder)1) },
                Limit = 2000, // Safe hard limit for initial history scan
                DtoOptions = new MediaBrowser.Controller.Dto.DtoOptions(false)
            };

            var qAudio = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Audio },
                Recursive = true,
                OrderBy = new[] { (ItemSortBy.DateCreated, (Jellyfin.Database.Implementations.Enums.SortOrder)1) },
                Limit = 2000, // Safe hard limit for initial history scan
                DtoOptions = new MediaBrowser.Controller.Dto.DtoOptions(false)
            };

            var qChannel = new InternalItemsQuery
            {
                // Uniquement requêter les éléments du Channel (VOD/IPTV) sans limiter le type,
                // car le plugin cible (ex: XFusion) gère ses propres types virtuels.
                Recursive = true,
                OrderBy = new[] { (ItemSortBy.DateCreated, (Jellyfin.Database.Implementations.Enums.SortOrder)1) },
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
                        // For episodes: allow if we haven't reached DatabaseCategoryLimit unique series yet
                        bool isNewSeries = !seriesSet.Contains(notif.SeriesId!);
                        if (isNewSeries && currentCount >= DatabaseCategoryLimit)
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
                        if (currentCount < DatabaseCategoryLimit)
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
            if (newNotifs.Count == 0 && oldDbIds.Count > 0)
            {
                _logger.LogWarning("Le scan de l'historique a retourné 0 élément (probablement en raison d'un démarrage du serveur où la bibliothèque n'est pas encore prête). La base de données existante ne sera pas effacée.");
                return;
            }

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

        private NotificationItem? CreateNotificationFromItem(BaseItem item)
        {
            if (!IsItemInEnabledLibrary(item))
            {
                return null;
            }

            // Ignorer les dossiers (ex: Les catégories racines VOD/Séries de XFusion)
            if (item.IsFolder || item is Folder)
            {
                return null;
            }

            // Ignorer les Extras (Openings, Endings, ThemeVideos, etc.)
            if (item.ExtraType.HasValue)
            {
                return null;
            }

            // Heuristique pour ignorer les thèmes et génériques (Openings/Endings)
            // Éliminer les éléments VOD courts, les thèmes musicaux et génériques mal classés
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

            // Exclure les musiques (Audio) servant de themes pour les séries/films (souvent Theme.mp3)
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
                string category = "Autres";
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
                    Name = item.Name ?? "Inconnu",
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
