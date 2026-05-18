using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;

namespace NotifySync
{
    /// <summary>
    /// A scheduled task to scan the library and populate the notification history.
    /// </summary>
    public class HistoryScanTask : IScheduledTask
    {
        /// <inheritdoc />
        public string Name => "NotifySync: History Scan";

        /// <inheritdoc />
        public string Key => "NotifySyncHistoryScanTask";

        /// <inheritdoc />
        public string Description => "Scanne la bibliothèque pour générer les notifications initiales (historique).";

        /// <inheritdoc />
        public string Category => "NotifySync";

        /// <inheritdoc />
        public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            if (NotificationManager.Instance == null)
            {
                return Task.CompletedTask;
            }

            // Only run on startup if the notifications DB is empty (fresh install).
            // Existing notifications carry IsUpgrade/UpgradeKind state that this scan
            // would wipe via ReplaceAllNotifications — real-time events (ItemAdded /
            // ItemUpdated / ItemRemoved) keep the table fresh in steady state, so an
            // automatic full rebuild on every restart is destructive AND unnecessary.
            // Manual click on "Régénérer l'historique" still triggers a full rebuild
            // (that's its purpose) — only the auto-startup path is gated here.
            if (NotificationManager.Instance.GetRecentNotifications().Count > 0)
            {
                return Task.CompletedTask;
            }

            return Task.Run(() => NotificationManager.Instance.ManualHistoryScan(progress, cancellationToken), cancellationToken);
        }

        /// <inheritdoc />
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfoType.StartupTrigger
                }
            };
        }
    }
}
