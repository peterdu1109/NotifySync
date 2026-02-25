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
        public string Name => "NotifySync : Scan de l'historique";

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

            return Task.Run(() => NotificationManager.Instance.ManualHistoryScan(progress, cancellationToken), cancellationToken);
        }

        /// <inheritdoc />
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            // Pas de trigger par défaut auto-planifié pour éviter les scans inutiles,
            // mais l'utilisateur peut en ajouter.
            return Array.Empty<TaskTriggerInfo>();
        }
    }
}
