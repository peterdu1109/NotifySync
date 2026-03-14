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
                            ParentIndexNumber INTEGER
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
                    ";
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de l'initialisation de la base SQLite.");
            }
        }

        /// <summary>
        /// Saves a collection of notifications to the database by replacing existing ones.
        /// Existing records that are not in this list are NOT deleted by this method.
        /// </summary>
        /// <param name="items">The items to save or update.</param>
        public void SaveNotifications(IEnumerable<NotificationItem> items)
        {
            var itemList = items.ToList();
            _logger.LogDebug("NotifySync : Sauvegarde de {Count} notifications.", itemList.Count);
            if (itemList.Count == 0)
            {
                return;
            }

            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                _logger.LogDebug("NotifySync : Connexion SQLite ouverte avec succès.");

                using var transaction = connection.BeginTransaction();
                try
                {
                    int insertedCount = 0;
                    using (var insertCmd = connection.CreateCommand())
                    {
                        insertCmd.Transaction = transaction;
                        insertCmd.CommandText = @"
                            INSERT OR REPLACE INTO Notifications (
                                Id, Name, Category, SeriesName, SeriesId, DateCreated, 
                                Type, RunTimeTicks, ProductionYear, BackdropImageTags, 
                                PrimaryImageTag, IndexNumber, ParentIndexNumber
                            ) VALUES (
                                @Id, @Name, @Category, @SeriesName, @SeriesId, @DateCreated, 
                                @Type, @RunTimeTicks, @ProductionYear, @Backdrop, 
                                @Primary, @Index, @ParentIndex
                            )";

                        var pId = insertCmd.Parameters.Add("@Id", SqliteType.Text);
                        var pName = insertCmd.Parameters.Add("@Name", SqliteType.Text);
                        var pCat = insertCmd.Parameters.Add("@Category", SqliteType.Text);
                        var pSName = insertCmd.Parameters.Add("@SeriesName", SqliteType.Text);
                        var pSId = insertCmd.Parameters.Add("@SeriesId", SqliteType.Text);
                        var pDate = insertCmd.Parameters.Add("@DateCreated", SqliteType.Text);
                        var pType = insertCmd.Parameters.Add("@Type", SqliteType.Text);
                        var pRun = insertCmd.Parameters.Add("@RunTimeTicks", SqliteType.Integer);
                        var pYear = insertCmd.Parameters.Add("@ProductionYear", SqliteType.Integer);
                        var pBack = insertCmd.Parameters.Add("@Backdrop", SqliteType.Text);
                        var pPrim = insertCmd.Parameters.Add("@Primary", SqliteType.Text);
                        var pIdx = insertCmd.Parameters.Add("@Index", SqliteType.Integer);
                        var pPIdx = insertCmd.Parameters.Add("@ParentIndex", SqliteType.Integer);

                        foreach (var item in itemList)
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

                            insertCmd.ExecuteNonQuery();
                            insertedCount++;
                        }
                    }

                    transaction.Commit();
                    _logger.LogDebug("NotifySync : {Count} lignes validées dans la base de données.", insertedCount);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "NotifySync : Erreur lors de l'exécution de la transaction. Annulation en cours.");
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la sauvegarde/mise à jour des notifications en SQLite.");
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
                    {
                        delCmd.Transaction = transaction;
                        delCmd.CommandText = "DELETE FROM Notifications WHERE Id = @Id";
                        var pId = delCmd.Parameters.Add("@Id", SqliteType.Text);

                        foreach (var id in ids)
                        {
                            if (string.IsNullOrEmpty(id))
                            {
                                continue;
                            }

                            pId.Value = id;
                            delCmd.ExecuteNonQuery();
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
                _logger.LogError(ex, "Erreur lors de la suppression de notifications ciblées en SQLite.");
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
                        using var insertCmd = connection.CreateCommand();
                        insertCmd.Transaction = transaction;
                        insertCmd.CommandText = @"
                            INSERT OR REPLACE INTO Notifications (
                                Id, Name, Category, SeriesName, SeriesId, DateCreated,
                                Type, RunTimeTicks, ProductionYear, BackdropImageTags,
                                PrimaryImageTag, IndexNumber, ParentIndexNumber
                            ) VALUES (
                                @Id, @Name, @Category, @SeriesName, @SeriesId, @DateCreated,
                                @Type, @RunTimeTicks, @ProductionYear, @Backdrop,
                                @Primary, @Index, @ParentIndex
                            )";

                        var pId = insertCmd.Parameters.Add("@Id", SqliteType.Text);
                        var pName = insertCmd.Parameters.Add("@Name", SqliteType.Text);
                        var pCat = insertCmd.Parameters.Add("@Category", SqliteType.Text);
                        var pSName = insertCmd.Parameters.Add("@SeriesName", SqliteType.Text);
                        var pSId = insertCmd.Parameters.Add("@SeriesId", SqliteType.Text);
                        var pDate = insertCmd.Parameters.Add("@DateCreated", SqliteType.Text);
                        var pType = insertCmd.Parameters.Add("@Type", SqliteType.Text);
                        var pRun = insertCmd.Parameters.Add("@RunTimeTicks", SqliteType.Integer);
                        var pYear = insertCmd.Parameters.Add("@ProductionYear", SqliteType.Integer);
                        var pBack = insertCmd.Parameters.Add("@Backdrop", SqliteType.Text);
                        var pPrim = insertCmd.Parameters.Add("@Primary", SqliteType.Text);
                        var pIdx = insertCmd.Parameters.Add("@Index", SqliteType.Integer);
                        var pPIdx = insertCmd.Parameters.Add("@ParentIndex", SqliteType.Integer);

                        foreach (var item in newList)
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
                            insertCmd.ExecuteNonQuery();
                        }
                    }

                    transaction.Commit();
                    _logger.LogInformation("NotifySync ReplaceAll : {Del} supprimées, {Ins} insérées en une seule transaction.", oldList.Count, newList.Count);
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotifySync : Erreur dans ReplaceAllNotifications (suppression+insertion atomique).");
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
                _logger.LogError(ex, "Erreur lors du VACUUM de la base SQLite.");
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
                        ParentIndexNumber = reader.IsDBNull(oParentIndex) ? null : reader.GetInt32(oParentIndex)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la lecture des notifications SQLite.");
            }

            return result;
        }

        /// <summary>
        /// Gets all user notification states for a given user.
        /// </summary>
        /// <param name="userId">The normalized user identifier.</param>
        /// <returns>A dictionary mapping notification IDs to their read/dismissed state.</returns>
        public Dictionary<string, (bool IsRead, bool IsDismissed)> GetUserStates(string userId)
        {
            var result = new Dictionary<string, (bool, bool)>();
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT NotificationId, IsRead, IsDismissed FROM UserNotificationState WHERE UserId = @UserId";
                cmd.Parameters.AddWithValue("@UserId", userId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    result[reader.GetString(0)] = (reader.GetInt32(1) != 0, reader.GetInt32(2) != 0);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotifySync : Erreur lors de la lecture des états de notification pour {UserId}.", userId);
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
                _logger.LogError(ex, "NotifySync : Erreur lors du marquage groupé comme lu pour {UserId}.", userId);
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
                _logger.LogError(ex, "NotifySync : Erreur lors de la suppression de la notification {NotifId} pour {UserId}.", notificationId, userId);
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
                _logger.LogError(ex, "NotifySync : Erreur lors de la suppression des états pour la notification {NotifId}.", notificationId);
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
                    _logger.LogInformation("NotifySync : {Count} états utilisateur orphelins purgés.", deleted);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotifySync : Erreur lors de la purge des états utilisateur orphelins.");
            }
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
