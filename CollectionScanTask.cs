using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;

namespace NotifySync
{
    /// <summary>
    /// A scheduled task that periodically scans monitored collections (BoxSets) for newly added items.
    /// </summary>
    public class CollectionScanTask : IScheduledTask
    {
        /// <inheritdoc />
        public string Name => "NotifySync: Collection Scan";

        /// <inheritdoc />
        public string Key => "NotifySyncCollectionScanTask";

        /// <inheritdoc />
        public string Description => "Scanne les collections surveillées pour détecter les nouveaux ajouts et créer des notifications.";

        /// <inheritdoc />
        public string Category => "NotifySync";

        /// <inheritdoc />
        public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            if (NotificationManager.Instance == null)
            {
                return Task.CompletedTask;
            }

            return Task.Run(() => NotificationManager.Instance.ScanCollections(progress, cancellationToken), cancellationToken);
        }

        /// <inheritdoc />
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfoType.IntervalTrigger,
                    IntervalTicks = TimeSpan.FromMinutes(15).Ticks
                }
            };
        }
    }
}
