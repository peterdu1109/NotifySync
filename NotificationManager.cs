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
            try
            {
                _dataLock.EnterWriteLock();
                _notifications = _db.GetAllNotifications().ToList();

                if (_notifications.Count == 0 && File.Exists(_jsonPath))
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
                                _notifications = oldNotifs;
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

            try
            {
                _dataLock.EnterWriteLock();
                int removed = _notifications.RemoveAll(n => n.Id == e.Item.Id.ToString());
                if (removed > 0)
                {
                    _db.SaveNotifications(_notifications);
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
                    try
                    {
                        _dataLock.EnterWriteLock();
                        foreach (var ni in newItems)
                        {
                            _notifications.RemoveAll(n => n.Id == ni.Id);
                            _notifications.Insert(0, ni);
                        }

                        if (_notifications.Count > GlobalRetentionLimit)
                        {
                            _notifications = _notifications.Take(GlobalRetentionLimit).ToList();
                        }

                        _db.SaveNotifications(_notifications);
                        Interlocked.Exchange(ref _versionCounter, DateTime.UtcNow.Ticks);
                    }
                    finally
                    {
                        if (_dataLock.IsWriteLockHeld)
                        {
                            _dataLock.ExitWriteLock();
                        }
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

            var query = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Episode },
                Recursive = true,
                OrderBy = new[] { (ItemSortBy.DateCreated, (Jellyfin.Database.Implementations.Enums.SortOrder)1) },
                Limit = 1000,
                DtoOptions = new MediaBrowser.Controller.Dto.DtoOptions(false)
            };

            var items = _libraryManager.GetItemList(query);
            var results = new List<NotificationItem>();
            int count = 0;

            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var notif = CreateNotificationFromItem(item);
                if (notif != null)
                {
                    results.Add(notif);
                }

                count++;
                progress?.Report((double)count / items.Count * 100);
            }

            try
            {
                _dataLock.EnterWriteLock();
                _notifications = results;
                _db.SaveNotifications(_notifications);
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

        private NotificationItem? CreateNotificationFromItem(BaseItem item)
        {
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

                return new NotificationItem
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
            }
            catch
            {
                return null;
            }
        }
    }
}
