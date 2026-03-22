using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
                    "NotifySync: Registered via File Transformation — automatic client.js injection into index.html (no file modification needed).");
            }
            else
            {
                _logger.LogWarning(
                    "NotifySync: The 'File Transformation' plugin is not installed. "
                    + "Install it for automatic injection, or manually add this line before </body> in index.html: {ScriptTag}",
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
                    _logger.LogDebug("NotifySync: Assembly 'Jellyfin.Plugin.FileTransformation' not found.");
                    return false;
                }

                // Find the static PluginInterface.RegisterTransformation(JObject) method
                Type? pluginInterface = ftAssembly.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface");
                if (pluginInterface == null)
                {
                    _logger.LogDebug("NotifySync: Type 'PluginInterface' not found in File Transformation.");
                    return false;
                }

                MethodInfo? registerMethod = pluginInterface.GetMethod("RegisterTransformation", BindingFlags.Static | BindingFlags.Public);
                if (registerMethod == null)
                {
                    _logger.LogDebug("NotifySync: Method 'RegisterTransformation' not found.");
                    return false;
                }

                // Build a JObject via reflection so we don't need a compile-time Newtonsoft.Json reference.
                // Jellyfin already loads Newtonsoft.Json at runtime.
                var paramType = registerMethod.GetParameters()[0].ParameterType;
                var payload = Activator.CreateInstance(paramType)!;
                var indexer = paramType.GetProperty("Item", new[] { typeof(string) })!;

                // JToken implicit conversion from string — use JValue to wrap strings
                var jValueType = paramType.Assembly.GetType("Newtonsoft.Json.Linq.JValue")!;
                object MakeJValue(string s) => Activator.CreateInstance(jValueType, new object[] { s })!;

                string thisAssemblyFullName = typeof(NotifySyncTransformation).Assembly.FullName!;

                indexer.SetValue(payload, MakeJValue("95655672-2342-4321-8291-321312312312"), new object[] { "id" });
                indexer.SetValue(payload, MakeJValue("index.html"), new object[] { "fileNamePattern" });
                indexer.SetValue(payload, MakeJValue(thisAssemblyFullName), new object[] { "callbackAssembly" });
                indexer.SetValue(payload, MakeJValue("NotifySync.NotifySyncTransformation"), new object[] { "callbackClass" });
                indexer.SetValue(payload, MakeJValue("Transform"), new object[] { "callbackMethod" });

                registerMethod.Invoke(null, new object[] { payload });

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "NotifySync: Error during File Transformation registration.");
                return false;
            }
        }
    }
}
