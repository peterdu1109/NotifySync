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
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                using var transaction = connection.BeginTransaction();
                try
                {
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

                        foreach (var item in items)
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
                }
                catch
                {
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
                while (reader.Read())
                {
                    result.Add(new NotificationItem
                    {
                        Id = reader.GetString(0),
                        Name = reader.GetString(1),
                        Category = reader.GetString(2),
                        SeriesName = reader.IsDBNull(3) ? null : reader.GetString(3),
                        SeriesId = reader.IsDBNull(4) ? null : reader.GetString(4),
                        DateCreated = DateTime.Parse(reader.GetString(5), CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind),
                        Type = reader.GetString(6),
                        RunTimeTicks = reader.IsDBNull(7) ? null : reader.GetInt64(7),
                        ProductionYear = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                        BackdropImageTags = JsonSerializer.Deserialize(reader.GetString(9), PluginJsonContext.Default.ListString) ?? new List<string>(),
                        PrimaryImageTag = reader.IsDBNull(10) ? null : reader.GetString(10),
                        IndexNumber = reader.IsDBNull(11) ? null : reader.GetInt32(11),
                        ParentIndexNumber = reader.IsDBNull(12) ? null : reader.GetInt32(12)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la lecture des notifications SQLite.");
            }

            return result;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}
