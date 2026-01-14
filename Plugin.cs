using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;
using NotifySync.Configuration;
using MediaBrowser.Model.IO;

namespace NotifySync
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages, IDisposable
    {
        public override string Name => "NotifySync";
        public override Guid Id => Guid.Parse("95655672-2342-4321-8291-321312312312");
        
        public static Plugin? Instance { get; private set; }

        private readonly NotificationManager _notificationManager;

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILibraryManager libraryManager, IFileSystem fileSystem, ILogger<NotificationManager> logger)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            // Instanciation unique du gestionnaire
            if (NotificationManager.Instance == null)
            {
                _notificationManager = new NotificationManager(libraryManager, logger, fileSystem);
            }
            else
            {
                _notificationManager = NotificationManager.Instance;
            }
        }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[] { new PluginPageInfo { Name = this.Name, EmbeddedResourcePath = GetType().Namespace + ".ConfigurationPage.html" } };
        }

        public void Dispose()
        {
            _notificationManager?.Dispose();
            Instance = null;
        }
    }
}