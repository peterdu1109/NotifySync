using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;

namespace NotifySync
{
    /// <summary>
    /// Periodic SQLite maintenance: compacts the notifications database (VACUUM)
    /// to reclaim free pages left behind by ongoing insert/delete churn. The DB is
    /// small, so the rebuild is quick; running it off the hot path (a background
    /// scheduled task) keeps it out of the request and event-handler paths.
    /// </summary>
    public class DatabaseMaintenanceTask : IScheduledTask
    {
        /// <inheritdoc />
        public string Name => "NotifySync: Database Maintenance";

        /// <inheritdoc />
        public string Key => "NotifySyncDatabaseMaintenanceTask";

        /// <inheritdoc />
        public string Description => "Compacte la base de données NotifySync (VACUUM) pour récupérer l'espace libéré par les ajouts/suppressions.";

        /// <inheritdoc />
        public string Category => "NotifySync";

        /// <inheritdoc />
        public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            var db = NotificationManager.Instance?.Db;
            if (db == null)
            {
                return Task.CompletedTask;
            }

            return Task.Run(() => db.Vacuum(), cancellationToken);
        }

        /// <inheritdoc />
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfoType.IntervalTrigger,
                    IntervalTicks = TimeSpan.FromDays(7).Ticks
                }
            };
        }
    }
}
