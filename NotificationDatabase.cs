using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace NotifySync
{
    /// <summary>
    /// Handles SQLite database operations for notification persistence.
    /// </summary>
    public sealed class NotificationDatabase : IDisposable
    {
        private readonly string _connectionString;
        private readonly ILogger _logger;
        private readonly string _dbPath;

        /// <summary>
        /// Initializes a new instance of the <see cref="NotificationDatabase"/> class.
        /// </summary>
        /// <param name="dataFolderPath">The path to the data folder.</param>
        /// <param name="logger">The logger.</param>
        public NotificationDatabase(string dataFolderPath, ILogger logger)
        {
            _logger = logger;

            if (!Directory.Exists(dataFolderPath))
            {
                Directory.CreateDirectory(dataFolderPath);
            }

            _dbPath = Path.Combine(dataFolderPath, "notifications.db");
            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = _dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = true
            }.ToString();

            InitializeDatabase();
        }

        /// <summary>
        /// Initializes the database schema and sets PRAGMAs.
        /// </summary>
        private void InitializeDatabase()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        PRAGMA journal_mode = WAL;
                        PRAGMA synchronous = NORMAL;
                        PRAGMA busy_timeout = 5000;
                    ";
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS Notifications (
                            Id TEXT PRIMARY KEY,
                            Name TEXT NOT NULL,
                            Category TEXT NOT NULL,
                            SeriesName TEXT,
                            SeriesId TEXT,
                            DateCreated TEXT NOT NULL,
                            Type TEXT NOT NULL,
                            RunTimeTicks INTEGER,
                            ProductionYear INTEGER,
                            BackdropImageTags TEXT,
                            PrimaryImageTag TEXT,
                            IndexNumber INTEGER,
                            ParentIndexNumber INTEGER,
                            IsUpgrade INTEGER NOT NULL DEFAULT 0,
                            FilePath TEXT,
                            UpgradeKind TEXT,
                            Size INTEGER
                        );
                        CREATE INDEX IF NOT EXISTS idx_notifications_date ON Notifications(DateCreated DESC);

                        CREATE TABLE IF NOT EXISTS UserNotificationState (
                            UserId TEXT NOT NULL,
                            NotificationId TEXT NOT NULL,
                            IsRead INTEGER NOT NULL DEFAULT 0,
                            IsDismissed INTEGER NOT NULL DEFAULT 0,
                            ReadAt TEXT,
                            DismissedAt TEXT,
                            PRIMARY KEY (UserId, NotificationId)
                        );
                        CREATE INDEX IF NOT EXISTS idx_uns_user ON UserNotificationState(UserId);
                        CREATE INDEX IF NOT EXISTS idx_uns_notif ON UserNotificationState(NotificationId);

                        CREATE TABLE IF NOT EXISTS CollectionSnapshots (
                            CollectionId TEXT NOT NULL,
                            ItemId TEXT NOT NULL,
                            PRIMARY KEY (CollectionId, ItemId)
                        );
                        CREATE INDEX IF NOT EXISTS idx_cs_collection ON CollectionSnapshots(CollectionId);

                        CREATE TABLE IF NOT EXISTS DeletedItems (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            ItemId TEXT NOT NULL,
                            Name TEXT NOT NULL,
                            Type TEXT NOT NULL,
                            SeriesName TEXT,
                            ProductionYear INTEGER,
                            IndexNumber INTEGER,
                            ParentIndexNumber INTEGER,
                            DeletedAt TEXT NOT NULL,
                            FilePath TEXT,
                            MatchedNotificationId TEXT,
                            Size INTEGER
                        );
                        CREATE INDEX IF NOT EXISTS idx_deleted_date ON DeletedItems(DeletedAt DESC);
                    ";
                    cmd.ExecuteNonQuery();
                }

                // Migration: add columns for existing databases (pre-5.5.7.x → present).
                MigrateAddColumn(connection, "Notifications", "IsUpgrade", "INTEGER NOT NULL DEFAULT 0");
                MigrateAddColumn(connection, "Notifications", "FilePath", "TEXT");
                MigrateAddColumn(connection, "Notifications", "UpgradeKind", "TEXT");
                MigrateAddColumn(connection, "Notifications", "Size", "INTEGER");
                MigrateAddColumn(connection, "DeletedItems", "IndexNumber", "INTEGER");
                MigrateAddColumn(connection, "DeletedItems", "ParentIndexNumber", "INTEGER");
                MigrateAddColumn(connection, "DeletedItems", "FilePath", "TEXT");
                MigrateAddColumn(connection, "DeletedItems", "MatchedNotificationId", "TEXT");
                MigrateAddColumn(connection, "DeletedItems", "Size", "INTEGER");

                // Drop columns no longer consumed by the slim classifier. Phase B Lite
                // (VideoWidth/Height/Container/MediaBitrate) was tried and removed;
                // DateModifiedTicks was used by the dropped "(sizeChanged && dateChanged)"
                // detection branch. SQLite ≥ 3.35 supports DROP COLUMN natively; Jellyfin
                // embeds a much newer build.
                // NOTE: Size is NOT dropped here — it was re-added in 5.7.9 as the rename
                // suppressor. Dropping it would delete the column right after creating it,
                // breaking every notification INSERT.
                MigrateDropColumn(connection, "Notifications", "VideoWidth");
                MigrateDropColumn(connection, "Notifications", "VideoHeight");
                MigrateDropColumn(connection, "Notifications", "Container");
                MigrateDropColumn(connection, "Notifications", "MediaBitrate");
                MigrateDropColumn(connection, "Notifications", "DateModifiedTicks");
                MigrateDropColumn(connection, "DeletedItems", "VideoWidth");
                MigrateDropColumn(connection, "DeletedItems", "VideoHeight");
                MigrateDropColumn(connection, "DeletedItems", "Container");
                MigrateDropColumn(connection, "DeletedItems", "MediaBitrate");

                // Clear UpgradeKind = "minor" rows from earlier beta1 builds. The slim
                // classifier no longer produces "minor"; leaving stale values around would
                // show a confusing MAJ • Mineur badge on items the new logic considers
                // unremarkable.
                using (var clearMinor = connection.CreateCommand())
                {
                    clearMinor.CommandText = "UPDATE Notifications SET UpgradeKind = NULL, IsUpgrade = 0 WHERE UpgradeKind = 'minor'";
                    clearMinor.ExecuteNonQuery();
                }

                // Clear all-zeros SeriesId values written before 5.7.19. An episode
                // scanned before its series was linked stored Guid.Empty as a string:
                // non-empty, so it passed IsNullOrEmpty checks and reached GetItemById,
                // which rejects an empty GUID (the response then failed during scans).
                // It also made every such episode collapse into one bogus series group.
                using (var clearEmptySeries = connection.CreateCommand())
                {
                    clearEmptySeries.CommandText = "UPDATE Notifications SET SeriesId = NULL WHERE SeriesId = '00000000-0000-0000-0000-000000000000'";
                    int cleared = clearEmptySeries.ExecuteNonQuery();
                    if (cleared > 0)
                    {
                        _logger.LogInformation("NotifySync: {Count} notification(s) had an unresolved series reference cleared.", cleared);
                    }
                }

                // Purge LiveTvProgram deletions accumulated by earlier builds. The plugin no
                // longer tracks them (they cycle naturally and don't serve any debug purpose
                // in the Deletions tab); the historic rows are pure noise.
                using (var purgeLiveTv = connection.CreateCommand())
                {
                    purgeLiveTv.CommandText = "DELETE FROM DeletedItems WHERE Type = 'LiveTvProgram'";
                    int purged = purgeLiveTv.ExecuteNonQuery();
                    if (purged > 0)
                    {
                        _logger.LogInformation("NotifySync: {Count} legacy LiveTvProgram deletions purged from history.", purged);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing SQLite database.");
            }
        }

        /// <summary>
        /// Returns the column ordinal for the given name, or -1 if the column doesn't exist
        /// (handles pre-migration databases gracefully).
        /// </summary>
        private static int TryGetOrdinal(SqliteDataReader reader, string columnName)
        {
            try
            {
                return reader.GetOrdinal(columnName);
            }
#pragma warning disable CA1031 // Microsoft.Data.Sqlite throws ArgumentOutOfRangeException (not IndexOutOfRangeException) for a missing column; catch broadly so a pre-migration DB degrades to -1 instead of throwing the whole read.
            catch (Exception)
#pragma warning restore CA1031
            {
                return -1;
            }
        }

        /// <summary>
        /// Attempts to add a column to an existing table. Silently ignores if the column already exists.
        /// </summary>
        private static void MigrateAddColumn(SqliteConnection connection, string table, string column, string definition)
        {
            try
            {
                using var cmd = connection.CreateCommand();

                // CA2100: All callers pass hardcoded string literals — no user input.
#pragma warning disable CA2100
                cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
#pragma warning restore CA2100
                cmd.ExecuteNonQuery();
            }
            catch (SqliteException)
            {
                // Column already exists — safe to ignore
            }
        }

        /// <summary>
        /// Attempts to drop a column from an existing table. Silently ignores if the column
        /// doesn't exist (e.g. fresh installs that never carried the legacy column).
        /// Requires SQLite ≥ 3.35; Jellyfin embeds a much newer version.
        /// </summary>
        private static void MigrateDropColumn(SqliteConnection connection, string table, string column)
        {
            try
            {
                using var cmd = connection.CreateCommand();

                // CA2100: All callers pass hardcoded string literals — no user input.
#pragma warning disable CA2100
                cmd.CommandText = $"ALTER TABLE {table} DROP COLUMN {column}";
#pragma warning restore CA2100
                cmd.ExecuteNonQuery();
            }
            catch (SqliteException)
            {
                // Column doesn't exist — safe to ignore
            }
        }

        /// <summary>
        /// Creates and configures a reusable INSERT OR REPLACE command for the Notifications table.
        /// Returns the command and a delegate that binds a <see cref="NotificationItem"/> to its parameters.
        /// </summary>
        private static (SqliteCommand Cmd, Action<NotificationItem> Bind) CreateInsertCommand(SqliteConnection connection, SqliteTransaction transaction)
        {
            var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                INSERT OR REPLACE INTO Notifications (
                    Id, Name, Category, SeriesName, SeriesId, DateCreated,
                    Type, RunTimeTicks, ProductionYear, BackdropImageTags,
                    PrimaryImageTag, IndexNumber, ParentIndexNumber,
                    IsUpgrade, FilePath, UpgradeKind, Size
                ) VALUES (
                    @Id, @Name, @Category, @SeriesName, @SeriesId, @DateCreated,
                    @Type, @RunTimeTicks, @ProductionYear, @Backdrop,
                    @Primary, @Index, @ParentIndex,
                    @IsUpgrade, @FilePath, @UpgradeKind, @Size
                )";

            var pId = cmd.Parameters.Add("@Id", SqliteType.Text);
            var pName = cmd.Parameters.Add("@Name", SqliteType.Text);
            var pCat = cmd.Parameters.Add("@Category", SqliteType.Text);
            var pSName = cmd.Parameters.Add("@SeriesName", SqliteType.Text);
            var pSId = cmd.Parameters.Add("@SeriesId", SqliteType.Text);
            var pDate = cmd.Parameters.Add("@DateCreated", SqliteType.Text);
            var pType = cmd.Parameters.Add("@Type", SqliteType.Text);
            var pRun = cmd.Parameters.Add("@RunTimeTicks", SqliteType.Integer);
            var pYear = cmd.Parameters.Add("@ProductionYear", SqliteType.Integer);
            var pBack = cmd.Parameters.Add("@Backdrop", SqliteType.Text);
            var pPrim = cmd.Parameters.Add("@Primary", SqliteType.Text);
            var pIdx = cmd.Parameters.Add("@Index", SqliteType.Integer);
            var pPIdx = cmd.Parameters.Add("@ParentIndex", SqliteType.Integer);
            var pUpgrade = cmd.Parameters.Add("@IsUpgrade", SqliteType.Integer);
            var pFilePath = cmd.Parameters.Add("@FilePath", SqliteType.Text);
            var pUpgradeKind = cmd.Parameters.Add("@UpgradeKind", SqliteType.Text);
            var pSize = cmd.Parameters.Add("@Size", SqliteType.Integer);

            void Bind(NotificationItem item)
            {
                pId.Value = item.Id ?? string.Empty;
                pName.Value = item.Name ?? string.Empty;
                pCat.Value = item.Category ?? string.Empty;
                pSName.Value = (object?)item.SeriesName ?? DBNull.Value;
                pSId.Value = (object?)item.SeriesId ?? DBNull.Value;
                pDate.Value = item.DateCreated.ToString("O");
                pType.Value = item.Type ?? string.Empty;
                pRun.Value = (object?)item.RunTimeTicks ?? DBNull.Value;
                pYear.Value = (object?)item.ProductionYear ?? DBNull.Value;
                pBack.Value = JsonSerializer.Serialize(item.BackdropImageTags ?? new List<string>(), PluginJsonContext.Default.ListString);
                pPrim.Value = (object?)item.PrimaryImageTag ?? DBNull.Value;
                pIdx.Value = (object?)item.IndexNumber ?? DBNull.Value;
                pPIdx.Value = (object?)item.ParentIndexNumber ?? DBNull.Value;
                pUpgrade.Value = item.IsUpgrade ? 1 : 0;
                pFilePath.Value = (object?)item.FilePath ?? DBNull.Value;
                pUpgradeKind.Value = (object?)item.UpgradeKind ?? DBNull.Value;
                pSize.Value = (object?)item.Size ?? DBNull.Value;
            }

            return (cmd, Bind);
        }

        /// <summary>
        /// Saves a collection of notifications to the database by replacing existing ones.
        /// Existing records that are not in this list are NOT deleted by this method.
        /// </summary>
        /// <param name="items">The items to save or update.</param>
        public void SaveNotifications(IEnumerable<NotificationItem> items)
        {
            var itemList = items.ToList();
            _logger.LogDebug("NotifySync: Saving {Count} notifications.", itemList.Count);
            if (itemList.Count == 0)
            {
                return;
            }

            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                _logger.LogDebug("NotifySync: SQLite connection opened successfully.");

                using var transaction = connection.BeginTransaction();
                try
                {
                    int insertedCount = 0;
                    var (insertCmd, bindItem) = CreateInsertCommand(connection, transaction);
                    using (insertCmd)
                    {
                        foreach (var item in itemList)
                        {
                            bindItem(item);
                            insertCmd.ExecuteNonQuery();
                            insertedCount++;
                        }
                    }

                    transaction.Commit();
                    _logger.LogDebug("NotifySync: {Count} rows committed to the database.", insertedCount);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "NotifySync: Error executing transaction. Rolling back.");
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving/updating notifications in SQLite.");
            }
        }

        /// <summary>
        /// Deletes multiple notifications from the database by their IDs.
        /// </summary>
        /// <param name="ids">List of notification IDs to delete.</param>
        public void DeleteNotifications(IEnumerable<string> ids)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var transaction = connection.BeginTransaction();
                try
                {
                    using (var delCmd = connection.CreateCommand())
                    using (var clearMatchCmd = connection.CreateCommand())
                    {
                        delCmd.Transaction = transaction;
                        delCmd.CommandText = "DELETE FROM Notifications WHERE Id = @Id";
                        var pId = delCmd.Parameters.Add("@Id", SqliteType.Text);

                        // Cascade: clear any DeletedItems row that pointed at this notification
                        // as its upgrade replacement, so the Deletions tab doesn't end up with
                        // links to notifications that no longer exist.
                        clearMatchCmd.Transaction = transaction;
                        clearMatchCmd.CommandText = "UPDATE DeletedItems SET MatchedNotificationId = NULL WHERE MatchedNotificationId = @MatchedId";
                        var pMatchedId = clearMatchCmd.Parameters.Add("@MatchedId", SqliteType.Text);

                        foreach (var id in ids)
                        {
                            if (string.IsNullOrEmpty(id))
                            {
                                continue;
                            }

                            pId.Value = id;
                            delCmd.ExecuteNonQuery();
                            pMatchedId.Value = id;
                            clearMatchCmd.ExecuteNonQuery();
                        }
                    }

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting targeted notifications in SQLite.");
            }
        }

        /// <summary>
        /// Atomically replaces all notifications: deletes old IDs and inserts new items in a single transaction.
        /// Prevents data loss if the process crashes between delete and insert.
        /// </summary>
        /// <param name="oldIds">IDs to delete.</param>
        /// <param name="newItems">Items to insert.</param>
        public void ReplaceAllNotifications(IEnumerable<string> oldIds, IEnumerable<NotificationItem> newItems)
        {
            var oldList = oldIds.Where(id => !string.IsNullOrEmpty(id)).ToList();
            var newList = newItems.ToList();

            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                using var transaction = connection.BeginTransaction();
                try
                {
                    // Phase 1: Delete old entries
                    if (oldList.Count > 0)
                    {
                        using var delCmd = connection.CreateCommand();
                        delCmd.Transaction = transaction;
                        delCmd.CommandText = "DELETE FROM Notifications WHERE Id = @Id";
                        var pId = delCmd.Parameters.Add("@Id", SqliteType.Text);
                        foreach (var id in oldList)
                        {
                            pId.Value = id;
                            delCmd.ExecuteNonQuery();
                        }
                    }

                    // Phase 2: Insert new entries
                    if (newList.Count > 0)
                    {
                        var (insertCmd, bindItem) = CreateInsertCommand(connection, transaction);
                        using (insertCmd)
                        {
                            foreach (var item in newList)
                            {
                                bindItem(item);
                                insertCmd.ExecuteNonQuery();
                            }
                        }
                    }

                    transaction.Commit();
                    _logger.LogInformation("NotifySync ReplaceAll: {Del} deleted, {Ins} inserted in a single transaction.", oldList.Count, newList.Count);
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotifySync: Error in ReplaceAllNotifications (atomic delete+insert).");
            }
        }

        /// <summary>
        /// Optimizes the SQLite database.
        /// </summary>
        public void Vacuum()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "VACUUM;";
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during SQLite VACUUM.");
            }
        }

        /// <summary>
        /// Retrieves all notifications from the database.
        /// </summary>
        /// <returns>A collection of notification items.</returns>
        public IReadOnlyCollection<NotificationItem> GetAllNotifications()
        {
            var result = new List<NotificationItem>();
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT * FROM Notifications ORDER BY DateCreated DESC";

                using var reader = cmd.ExecuteReader();

                // Resolve ordinals once for robustness against schema changes
                int oId = reader.GetOrdinal("Id");
                int oName = reader.GetOrdinal("Name");
                int oCategory = reader.GetOrdinal("Category");
                int oSeriesName = reader.GetOrdinal("SeriesName");
                int oSeriesId = reader.GetOrdinal("SeriesId");
                int oDateCreated = reader.GetOrdinal("DateCreated");
                int oType = reader.GetOrdinal("Type");
                int oRunTimeTicks = reader.GetOrdinal("RunTimeTicks");
                int oProductionYear = reader.GetOrdinal("ProductionYear");
                int oBackdrop = reader.GetOrdinal("BackdropImageTags");
                int oPrimary = reader.GetOrdinal("PrimaryImageTag");
                int oIndex = reader.GetOrdinal("IndexNumber");
                int oParentIndex = reader.GetOrdinal("ParentIndexNumber");
                int oIsUpgrade = reader.GetOrdinal("IsUpgrade");
                int oFilePath = reader.GetOrdinal("FilePath");
                int oUpgradeKind = TryGetOrdinal(reader, "UpgradeKind");
                int oSize = TryGetOrdinal(reader, "Size");

                while (reader.Read())
                {
                    result.Add(new NotificationItem
                    {
                        Id = reader.GetString(oId),
                        Name = reader.GetString(oName),
                        Category = reader.GetString(oCategory),
                        SeriesName = reader.IsDBNull(oSeriesName) ? null : reader.GetString(oSeriesName),
                        SeriesId = reader.IsDBNull(oSeriesId) ? null : reader.GetString(oSeriesId),
                        DateCreated = DateTime.Parse(reader.GetString(oDateCreated), CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
                        Type = reader.GetString(oType),
                        RunTimeTicks = reader.IsDBNull(oRunTimeTicks) ? null : reader.GetInt64(oRunTimeTicks),
                        ProductionYear = reader.IsDBNull(oProductionYear) ? null : reader.GetInt32(oProductionYear),
                        BackdropImageTags = reader.IsDBNull(oBackdrop) ? new List<string>() : JsonSerializer.Deserialize(reader.GetString(oBackdrop), PluginJsonContext.Default.ListString) ?? new List<string>(),
                        PrimaryImageTag = reader.IsDBNull(oPrimary) ? null : reader.GetString(oPrimary),
                        IndexNumber = reader.IsDBNull(oIndex) ? null : reader.GetInt32(oIndex),
                        ParentIndexNumber = reader.IsDBNull(oParentIndex) ? null : reader.GetInt32(oParentIndex),
                        IsUpgrade = !reader.IsDBNull(oIsUpgrade) && reader.GetInt32(oIsUpgrade) != 0,
                        FilePath = reader.IsDBNull(oFilePath) ? null : reader.GetString(oFilePath),
                        UpgradeKind = (oUpgradeKind < 0 || reader.IsDBNull(oUpgradeKind)) ? null : reader.GetString(oUpgradeKind),
                        Size = (oSize < 0 || reader.IsDBNull(oSize)) ? null : reader.GetInt64(oSize)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading notifications from SQLite.");
            }

            return result;
        }

        /// <summary>
        /// Gets all user notification states for a given user.
        /// </summary>
        /// <param name="userId">The normalized user identifier.</param>
        /// <returns>A dictionary mapping notification IDs to their read/dismissed state.</returns>
        public Dictionary<string, (bool IsRead, bool IsDismissed, DateTime? ReadAt)> GetUserStates(string userId)
        {
            var result = new Dictionary<string, (bool, bool, DateTime?)>();
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                using var cmd = connection.CreateCommand();

                // ReadAt is returned so the caller can tell a notification that was read
                // *before* it was bumped by an upgrade from one read after — an upgraded
                // item must light the counter up again. It is always written alongside
                // IsRead = 1, so it is non-null whenever IsRead is true.
                cmd.CommandText = "SELECT NotificationId, IsRead, IsDismissed, ReadAt FROM UserNotificationState WHERE UserId = @UserId";
                cmd.Parameters.AddWithValue("@UserId", userId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    DateTime? readAt = null;
                    if (!reader.IsDBNull(3)
                        && DateTime.TryParse(reader.GetString(3), CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                    {
                        readAt = parsed;
                    }

                    result[reader.GetString(0)] = (reader.GetInt32(1) != 0, reader.GetInt32(2) != 0, readAt);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotifySync: Error reading notification states for {UserId}.", userId);
            }

            return result;
        }

        /// <summary>
        /// Marks multiple notifications as read for a user (batch UPSERT).
        /// </summary>
        /// <param name="userId">The normalized user identifier.</param>
        /// <param name="notificationIds">The notification IDs to mark as read.</param>
        public void BulkSetRead(string userId, IEnumerable<string> notificationIds)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                using var transaction = connection.BeginTransaction();
                try
                {
                    using var cmd = connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO UserNotificationState (UserId, NotificationId, IsRead, ReadAt)
                        VALUES (@UserId, @NotifId, 1, @Now)
                        ON CONFLICT(UserId, NotificationId)
                        DO UPDATE SET IsRead = 1, ReadAt = @Now";
                    var pUserId = cmd.Parameters.Add("@UserId", SqliteType.Text);
                    var pNotifId = cmd.Parameters.Add("@NotifId", SqliteType.Text);
                    var pNow = cmd.Parameters.Add("@Now", SqliteType.Text);
                    pUserId.Value = userId;
                    pNow.Value = DateTime.UtcNow.ToString("O");

                    foreach (var id in notificationIds)
                    {
                        if (!string.IsNullOrEmpty(id))
                        {
                            pNotifId.Value = id;
                            cmd.ExecuteNonQuery();
                        }
                    }

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotifySync: Error during bulk mark-read for {UserId}.", userId);
            }
        }

        /// <summary>
        /// Marks a single notification as dismissed for a user.
        /// </summary>
        /// <param name="userId">The normalized user identifier.</param>
        /// <param name="notificationId">The notification ID to dismiss.</param>
        public void SetItemDismissed(string userId, string notificationId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO UserNotificationState (UserId, NotificationId, IsDismissed, DismissedAt)
                    VALUES (@UserId, @NotifId, 1, @Now)
                    ON CONFLICT(UserId, NotificationId)
                    DO UPDATE SET IsDismissed = 1, DismissedAt = @Now";
                cmd.Parameters.AddWithValue("@UserId", userId);
                cmd.Parameters.AddWithValue("@NotifId", notificationId);
                cmd.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("O"));
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotifySync: Error dismissing notification {NotifId} for {UserId}.", notificationId, userId);
            }
        }

        /// <summary>
        /// Marks multiple notifications as dismissed for a user in a single transaction.
        /// </summary>
        /// <param name="userId">The normalized user identifier.</param>
        /// <param name="notificationIds">The notification IDs to dismiss.</param>
        public void BulkSetDismissed(string userId, IEnumerable<string> notificationIds)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                using var transaction = connection.BeginTransaction();
                try
                {
                    using var cmd = connection.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        INSERT INTO UserNotificationState (UserId, NotificationId, IsDismissed, DismissedAt)
                        VALUES (@UserId, @NotifId, 1, @Now)
                        ON CONFLICT(UserId, NotificationId)
                        DO UPDATE SET IsDismissed = 1, DismissedAt = @Now";
                    var pUserId = cmd.Parameters.Add("@UserId", SqliteType.Text);
                    var pNotifId = cmd.Parameters.Add("@NotifId", SqliteType.Text);
                    var pNow = cmd.Parameters.Add("@Now", SqliteType.Text);
                    pUserId.Value = userId;
                    pNow.Value = DateTime.UtcNow.ToString("O");

                    foreach (var id in notificationIds)
                    {
                        if (!string.IsNullOrEmpty(id))
                        {
                            pNotifId.Value = id;
                            cmd.ExecuteNonQuery();
                        }
                    }

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotifySync: Error during bulk dismiss for {UserId}.", userId);
            }
        }

        /// <summary>
        /// Deletes all user states for a specific notification (cleanup when notification is removed globally).
        /// </summary>
        /// <param name="notificationId">The notification ID whose states should be purged.</param>
        public void DeleteStatesForNotification(string notificationId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM UserNotificationState WHERE NotificationId = @NotifId";
                cmd.Parameters.AddWithValue("@NotifId", notificationId);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotifySync: Error deleting states for notification {NotifId}.", notificationId);
            }
        }

        /// <summary>
        /// Removes orphaned user notification states where the notification no longer exists.
        /// </summary>
        public void PurgeOrphanedStates()
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    DELETE FROM UserNotificationState
                    WHERE NotificationId NOT IN (SELECT Id FROM Notifications)";
                int deleted = cmd.ExecuteNonQuery();
                if (deleted > 0)
                {
                    _logger.LogInformation("NotifySync: {Count} orphaned user states purged.", deleted);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotifySync: Error purging orphaned user states.");
            }
        }

        /// <summary>
        /// Gets the set of known item IDs for a given collection snapshot.
        /// </summary>
        /// <param name="collectionId">The collection (BoxSet) GUID string.</param>
        /// <returns>A set of item ID strings previously stored for this collection.</returns>
        public HashSet<string> GetCollectionSnapshot(string collectionId)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT ItemId FROM CollectionSnapshots WHERE CollectionId = @CollectionId";
                cmd.Parameters.AddWithValue("@CollectionId", collectionId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    result.Add(reader.GetString(0));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotifySync: Error reading snapshot for collection {CollectionId}.", collectionId);
            }

            return result;
        }

        /// <summary>
        /// Replaces the snapshot for a collection with the current set of item IDs.
        /// </summary>
        /// <param name="collectionId">The collection (BoxSet) GUID string.</param>
        /// <param name="currentItemIds">The current item IDs in the collection.</param>
        public void UpdateCollectionSnapshot(string collectionId, IEnumerable<string> currentItemIds)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                using var transaction = connection.BeginTransaction();
                try
                {
                    using (var delCmd = connection.CreateCommand())
                    {
                        delCmd.Transaction = transaction;
                        delCmd.CommandText = "DELETE FROM CollectionSnapshots WHERE CollectionId = @CollectionId";
                        delCmd.Parameters.AddWithValue("@CollectionId", collectionId);
                        delCmd.ExecuteNonQuery();
                    }

                    using (var insCmd = connection.CreateCommand())
                    {
                        insCmd.Transaction = transaction;
                        insCmd.CommandText = "INSERT INTO CollectionSnapshots (CollectionId, ItemId) VALUES (@CollectionId, @ItemId)";
                        var pCid = insCmd.Parameters.Add("@CollectionId", SqliteType.Text);
                        var pIid = insCmd.Parameters.Add("@ItemId", SqliteType.Text);
                        pCid.Value = collectionId;

                        foreach (var itemId in currentItemIds)
                        {
                            pIid.Value = itemId;
                            insCmd.ExecuteNonQuery();
                        }
                    }

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotifySync: Error updating snapshot for collection {CollectionId}.", collectionId);
            }
        }

        /// <summary>
        /// Removes snapshots for collections that are no longer in the active configuration.
        /// </summary>
        /// <param name="activeCollectionIds">The set of collection IDs still being monitored.</param>
        public void RemoveStaleCollectionSnapshots(IReadOnlyCollection<string> activeCollectionIds)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                if (activeCollectionIds.Count == 0)
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = "DELETE FROM CollectionSnapshots";
                    cmd.ExecuteNonQuery();
                    return;
                }

                using var delCmd = connection.CreateCommand();
                var paramNames = new List<string>();
                for (int i = 0; i < activeCollectionIds.Count; i++)
                {
                    paramNames.Add($"@cid{i}");
                }

                // paramNames only contains generated @cid0, @cid1, ... — no user input in the SQL text
#pragma warning disable CA2100
                delCmd.CommandText = $"DELETE FROM CollectionSnapshots WHERE CollectionId NOT IN ({string.Join(", ", paramNames)})";
#pragma warning restore CA2100
                int idx = 0;
                foreach (var cid in activeCollectionIds)
                {
                    delCmd.Parameters.AddWithValue($"@cid{idx}", cid);
                    idx++;
                }

                delCmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotifySync: Error cleaning up stale collection snapshots.");
            }
        }

        /// <summary>
        /// Records a deleted item in the DeletedItems table.
        /// </summary>
        /// <param name="itemId">The Jellyfin item ID.</param>
        /// <param name="name">The item name.</param>
        /// <param name="type">The item type (Movie, Episode, Audio, etc.).</param>
        /// <param name="seriesName">The series name, if applicable.</param>
        /// <param name="productionYear">The production year, if applicable.</param>
        /// <param name="indexNumber">The episode number, if applicable.</param>
        /// <param name="parentIndexNumber">The season number, if applicable.</param>
        /// <param name="filePath">The file path of the deleted source, used by ClassifyUpgrade.</param>
        /// <param name="size">The deleted file's size in bytes, used by ClassifyUpgrade as a rename suppressor.</param>
        public void SaveDeletedItem(string itemId, string name, string type, string? seriesName, int? productionYear, int? indexNumber = null, int? parentIndexNumber = null, string? filePath = null, long? size = null)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO DeletedItems (ItemId, Name, Type, SeriesName, ProductionYear, IndexNumber, ParentIndexNumber, DeletedAt, FilePath, Size)
                    VALUES (@ItemId, @Name, @Type, @SeriesName, @ProductionYear, @IndexNumber, @ParentIndexNumber, @DeletedAt, @FilePath, @Size)";
                cmd.Parameters.AddWithValue("@ItemId", itemId);
                cmd.Parameters.AddWithValue("@Name", name);
                cmd.Parameters.AddWithValue("@Type", type);
                cmd.Parameters.AddWithValue("@SeriesName", (object?)seriesName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ProductionYear", (object?)productionYear ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@IndexNumber", (object?)indexNumber ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ParentIndexNumber", (object?)parentIndexNumber ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@DeletedAt", DateTime.UtcNow.ToString("O"));
                cmd.Parameters.AddWithValue("@FilePath", (object?)filePath ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Size", (object?)size ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotifySync: Error saving deleted item {Name}.", name);
            }
        }

        /// <summary>
        /// Returns the paths of container items — series and season folders — removed within
        /// the given window.
        /// <para>
        /// When a series is moved to another library, Jellyfin announces the removal of the
        /// SERIES FOLDER and never of its episodes; the episodes simply reappear as adds. The
        /// folder's path is therefore the only trace of where those files came from, and its
        /// last segment is unchanged by the move — which makes it the one link between the two
        /// halves of the operation that needs no metadata at all.
        /// </para>
        /// </summary>
        /// <param name="cutoffUtc">Oldest removal still eligible.</param>
        /// <returns>The recorded containers.</returns>
        public DeletedFolder[] GetDeletedFolderPathsSince(DateTime cutoffUtc)
        {
            var result = new List<DeletedFolder>();
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT FilePath, Type FROM DeletedItems
                    WHERE FilePath IS NOT NULL AND FilePath <> ''
                      AND Type IN ('Series', 'Season', 'Folder')
                      AND DeletedAt > @Cutoff";
                cmd.Parameters.AddWithValue("@Cutoff", cutoffUtc.ToString("O"));
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    if (!reader.IsDBNull(0))
                    {
                        result.Add(new DeletedFolder(reader.GetString(0), reader.IsDBNull(1) ? string.Empty : reader.GetString(1)));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotifySync: Error reading recently deleted folders.");
            }

            return result.ToArray();
        }

        /// <summary>
        /// Retrieves deleted items with pagination, ordered by most recent first.
        /// </summary>
        /// <param name="limit">Maximum number of records to return.</param>
        /// <param name="offset">Number of records to skip.</param>
        /// <returns>A list of deleted item records.</returns>
        public IReadOnlyList<DeletedItemRecord> GetDeletedItems(int limit = 200, int offset = 0)
        {
            var result = new List<DeletedItemRecord>();
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                using var cmd = connection.CreateCommand();
                // Container rows (series/season folders) are bookkeeping for move detection,
                // not media the user deleted — they would only clutter the admin tab.
                cmd.CommandText = @"
                    SELECT Id, ItemId, Name, Type, SeriesName, ProductionYear, IndexNumber, ParentIndexNumber, DeletedAt, FilePath, MatchedNotificationId
                    FROM DeletedItems
                    WHERE Type NOT IN ('Series', 'Season', 'Folder')
                    ORDER BY DeletedAt DESC LIMIT @Limit OFFSET @Offset";
                cmd.Parameters.AddWithValue("@Limit", limit > 0 ? limit : 200);
                cmd.Parameters.AddWithValue("@Offset", offset >= 0 ? offset : 0);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    result.Add(new DeletedItemRecord
                    {
                        Id = reader.GetInt64(0),
                        ItemId = reader.GetString(1),
                        Name = reader.GetString(2),
                        Type = reader.GetString(3),
                        SeriesName = reader.IsDBNull(4) ? null : reader.GetString(4),
                        ProductionYear = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                        IndexNumber = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                        ParentIndexNumber = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                        DeletedAt = DateTime.Parse(reader.GetString(8), CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
                        FilePath = reader.IsDBNull(9) ? null : reader.GetString(9),
                        MatchedNotificationId = reader.IsDBNull(10) ? null : reader.GetString(10)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotifySync: Error reading deleted items.");
            }

            return result;
        }

        /// <summary>
        /// Returns the most recent deleted record that matches the given identity (within 7 days), or null.
        /// Used by <c>ProcessBuffer</c> to feed ClassifyUpgrade with the &quot;before&quot; file properties.
        /// </summary>
        /// <param name="name">The item name.</param>
        /// <param name="type">The item type.</param>
        /// <param name="productionYear">The production year.</param>
        /// <param name="seriesName">The series name (for episodes).</param>
        /// <param name="indexNumber">The episode number (for episodes).</param>
        /// <param name="parentIndexNumber">The season number (for episodes).</param>
        /// <returns>The matched <see cref="DeletedItemRecord"/> or null.</returns>
        public DeletedItemRecord? TryGetDeletedMatch(string name, string type, int? productionYear, string? seriesName = null, int? indexNumber = null, int? parentIndexNumber = null)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                using var cmd = connection.CreateCommand();
                var cutoff = DateTime.UtcNow.AddDays(-7).ToString("O");

                // Episodes: match by SeriesName + Season + Episode (name can change: TBA → real title, VO → VF)
                if (type == "Episode" && !string.IsNullOrEmpty(seriesName) && indexNumber.HasValue && parentIndexNumber.HasValue)
                {
                    cmd.CommandText = @"
                        SELECT Id, ItemId, Name, Type, SeriesName, ProductionYear, IndexNumber, ParentIndexNumber, DeletedAt, FilePath, MatchedNotificationId, Size
                        FROM DeletedItems
                        WHERE Type = 'Episode' AND SeriesName = @SeriesName
                        AND IndexNumber = @IndexNumber AND ParentIndexNumber = @ParentIndexNumber
                        AND DeletedAt > @Cutoff
                        ORDER BY DeletedAt DESC LIMIT 1";
                    cmd.Parameters.AddWithValue("@SeriesName", seriesName);
                    cmd.Parameters.AddWithValue("@IndexNumber", indexNumber.Value);
                    cmd.Parameters.AddWithValue("@ParentIndexNumber", parentIndexNumber.Value);
                    cmd.Parameters.AddWithValue("@Cutoff", cutoff);
                }
                else
                {
                    cmd.CommandText = @"
                        SELECT Id, ItemId, Name, Type, SeriesName, ProductionYear, IndexNumber, ParentIndexNumber, DeletedAt, FilePath, MatchedNotificationId, Size
                        FROM DeletedItems
                        WHERE Name = @Name AND Type = @Type AND DeletedAt > @Cutoff
                        AND (@Year IS NULL OR ProductionYear IS NULL OR ProductionYear = @Year)
                        ORDER BY DeletedAt DESC LIMIT 1";
                    cmd.Parameters.AddWithValue("@Name", name);
                    cmd.Parameters.AddWithValue("@Type", type);
                    cmd.Parameters.AddWithValue("@Cutoff", cutoff);
                    cmd.Parameters.AddWithValue("@Year", (object?)productionYear ?? DBNull.Value);
                }

                using var reader = cmd.ExecuteReader();
                if (!reader.Read())
                {
                    return null;
                }

                return new DeletedItemRecord
                {
                    Id = reader.GetInt64(0),
                    ItemId = reader.GetString(1),
                    Name = reader.GetString(2),
                    Type = reader.GetString(3),
                    SeriesName = reader.IsDBNull(4) ? null : reader.GetString(4),
                    ProductionYear = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    IndexNumber = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    ParentIndexNumber = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    DeletedAt = DateTime.Parse(reader.GetString(8), CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
                    FilePath = reader.IsDBNull(9) ? null : reader.GetString(9),
                    MatchedNotificationId = reader.IsDBNull(10) ? null : reader.GetString(10),
                    Size = reader.IsDBNull(11) ? null : reader.GetInt64(11)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotifySync: Error fetching deleted item match for {Name}.", name);
                return null;
            }
        }

        /// <summary>
        /// Marks a deleted item as matched to a replacement notification. Called when
        /// <c>TryGetDeletedMatch</c> resolves a re-import to this deleted row, so the admin
        /// Deletions tab can show a matched / orphan status.
        /// </summary>
        /// <param name="deletedItemId">The auto-increment ID of the deleted row.</param>
        /// <param name="notificationId">The ID of the new notification that replaced it.</param>
        public void MarkDeletedAsMatched(long deletedItemId, string notificationId)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE DeletedItems SET MatchedNotificationId = @NotifId WHERE Id = @Id";
                cmd.Parameters.AddWithValue("@NotifId", notificationId);
                cmd.Parameters.AddWithValue("@Id", deletedItemId);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotifySync: Error marking deleted item {Id} as matched.", deletedItemId);
            }
        }

        /// <summary>
        /// Removes deleted item records older than the specified number of days.
        /// </summary>
        /// <param name="retentionDays">The number of days to retain records.</param>
        /// <returns>The number of purged records.</returns>
        public int PurgeExpiredDeletedItems(int retentionDays)
        {
            int deleted = 0;
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                using var cmd = connection.CreateCommand();
                var cutoff = DateTime.UtcNow.AddDays(-retentionDays).ToString("O");
                cmd.CommandText = "DELETE FROM DeletedItems WHERE DeletedAt < @Cutoff";
                cmd.Parameters.AddWithValue("@Cutoff", cutoff);
                deleted = cmd.ExecuteNonQuery();
                if (deleted > 0)
                {
                    _logger.LogInformation("NotifySync: {Count} expired deleted items purged (retention {Days}d).", deleted, retentionDays);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotifySync: Error purging expired deleted items.");
            }

            return deleted;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            using (var conn = new SqliteConnection(_connectionString))
            {
                SqliteConnection.ClearPool(conn);
            }

            GC.SuppressFinalize(this);
        }
    }
}
