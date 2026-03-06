using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace NotifySync
{
    /// <summary>
    /// Background service that registers a File Transformation callback
    /// to inject client.js into index.html at the HTTP level.
    /// Falls back with a helpful log message if File Transformation is not installed.
    /// </summary>
    public sealed class NotifySyncEntryPoint : IHostedService
    {
        private const string ScriptTag = "<script src=\"/NotifySync/client.js\"></script>";

        private readonly ILogger<NotifySyncEntryPoint> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="NotifySyncEntryPoint"/> class.
        /// </summary>
        /// <param name="logger">The logger.</param>
        public NotifySyncEntryPoint(ILogger<NotifySyncEntryPoint> logger)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (TryRegisterFileTransformation())
            {
                _logger.LogInformation(
                    "NotifySync: Enregistré via File Transformation — injection automatique de client.js dans index.html (aucune modification de fichier nécessaire).");
            }
            else
            {
                _logger.LogWarning(
                    "NotifySync: Le plugin 'File Transformation' n'est pas installé. "
                    + "Installez-le pour une injection automatique, ou ajoutez manuellement cette ligne avant </body> dans index.html : {ScriptTag}",
                    ScriptTag);
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>
        /// Tries to find the File Transformation plugin via reflection and register
        /// our transformation callback. Returns true on success.
        /// </summary>
        private bool TryRegisterFileTransformation()
        {
            try
            {
                // Find the File Transformation assembly loaded by Jellyfin
                Assembly? ftAssembly = AssemblyLoadContext.All
                    .SelectMany(ctx => ctx.Assemblies)
                    .FirstOrDefault(a => a.GetName().Name == "Jellyfin.Plugin.FileTransformation");

                if (ftAssembly == null)
                {
                    _logger.LogDebug("NotifySync: Assembly 'Jellyfin.Plugin.FileTransformation' non trouvée.");
                    return false;
                }

                // Find the static PluginInterface.RegisterTransformation(JObject) method
                Type? pluginInterface = ftAssembly.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface");
                if (pluginInterface == null)
                {
                    _logger.LogDebug("NotifySync: Type 'PluginInterface' introuvable dans File Transformation.");
                    return false;
                }

                MethodInfo? registerMethod = pluginInterface.GetMethod("RegisterTransformation", BindingFlags.Static | BindingFlags.Public);
                if (registerMethod == null)
                {
                    _logger.LogDebug("NotifySync: Méthode 'RegisterTransformation' introuvable.");
                    return false;
                }

                // Build the registration payload
                // File Transformation will call NotifySyncTransformation.Transform via reflection
                string thisAssemblyFullName = typeof(NotifySyncTransformation).Assembly.FullName!;

                JObject payload = new JObject
                {
                    ["id"] = "95655672-2342-4321-8291-321312312312",
                    ["fileNamePattern"] = "index.html",
                    ["callbackAssembly"] = thisAssemblyFullName,
                    ["callbackClass"] = "NotifySync.NotifySyncTransformation",
                    ["callbackMethod"] = "Transform",
                };

                registerMethod.Invoke(null, new object[] { payload });

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "NotifySync: Erreur lors de l'enregistrement File Transformation.");
                return false;
            }
        }
    }
}
