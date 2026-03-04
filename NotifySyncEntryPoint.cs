using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NotifySync
{
    /// <summary>
    /// Background service that injects the client.js script tag
    /// into Jellyfin's index.html at server startup.
    /// </summary>
    public sealed class NotifySyncEntryPoint : IHostedService
    {
        private const string ScriptTag = "<script src=\"/NotifySync/client.js\"></script>";

        private readonly IApplicationPaths _appPaths;
        private readonly ILogger<NotifySyncEntryPoint> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="NotifySyncEntryPoint"/> class.
        /// </summary>
        /// <param name="appPaths">The application paths.</param>
        /// <param name="logger">The logger.</param>
        public NotifySyncEntryPoint(IApplicationPaths appPaths, ILogger<NotifySyncEntryPoint> logger)
        {
            _appPaths = appPaths;
            _logger = logger;
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                InjectScript();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NotifySync: Erreur lors de l'injection automatique dans index.html.");
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private void InjectScript()
        {
            var webPath = _appPaths.WebPath;
            if (string.IsNullOrEmpty(webPath) || !Directory.Exists(webPath))
            {
                _logger.LogWarning("NotifySync: Dossier 'jellyfin-web' introuvable. Injection ignorée.");
                return;
            }

            var indexPath = Path.Combine(webPath, "index.html");
            if (!File.Exists(indexPath))
            {
                _logger.LogWarning("NotifySync: index.html introuvable dans {WebPath}.", webPath);
                return;
            }

            string html = File.ReadAllText(indexPath);

            if (html.Contains(ScriptTag, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("NotifySync: Script client déjà présent dans index.html.");
                return;
            }

            int bodyIndex = html.IndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            if (bodyIndex < 0)
            {
                _logger.LogWarning("NotifySync: Balise </body> introuvable dans index.html.");
                return;
            }

            html = html.Insert(bodyIndex, "    " + ScriptTag + "\n");
            File.WriteAllText(indexPath, html);
            _logger.LogInformation("NotifySync: Injection automatique du script client réussie !");
        }
    }
}
