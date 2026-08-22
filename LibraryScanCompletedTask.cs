using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;

namespace NotifySync
{
    /// <summary>
    /// Notifies the manager when Jellyfin finishes validating the media library.
    /// <para>
    /// Move detection needs to know how long ago a series folder disappeared, and a fixed
    /// duration is a poor answer: a scan of a multi-terabyte library can put the source and the
    /// destination tens of minutes apart, while a short window is what keeps a genuine addition
    /// from being mistaken for a move. The scan boundary is the honest bound — both halves of a
    /// move happen inside one scan — and this is how Jellyfin reports it. Registered
    /// automatically: the host collects every <see cref="ILibraryPostScanTask"/> it can find,
    /// plugin assemblies included, and runs them once after the whole library is validated.
    /// </para>
    /// </summary>
    public class LibraryScanCompletedTask : ILibraryPostScanTask
    {
        /// <inheritdoc />
        public Task Run(IProgress<double> progress, CancellationToken cancellationToken)
        {
            NotificationManager.Instance?.OnLibraryScanCompleted();
            progress?.Report(100);
            return Task.CompletedTask;
        }
    }
}
