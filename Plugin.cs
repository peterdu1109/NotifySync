using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace NotifySync
{
    /// <summary>
    /// The main plugin class for NotifySync.
    /// </summary>
    public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages, IDisposable
    {
        private NotificationManager? _notificationManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="Plugin"/> class.
        /// </summary>
        /// <param name="applicationPaths">The application paths.</param>
        /// <param name="xmlSerializer">The XML serializer.</param>
        /// <param name="libraryManager">The library manager.</param>
        /// <param name="loggerFactory">The logger factory.</param>
        /// <param name="fileSystem">The file system.</param>
        public Plugin(
            IApplicationPaths applicationPaths,
            IXmlSerializer xmlSerializer,
            ILibraryManager libraryManager,
            ILoggerFactory loggerFactory,
            MediaBrowser.Model.IO.IFileSystem fileSystem)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            _notificationManager = new NotificationManager(libraryManager, loggerFactory.CreateLogger<NotificationManager>(), fileSystem);
        }

        /// <summary>
        /// Gets the singleton instance.
        /// </summary>
        public static Plugin? Instance { get; private set; }

        /// <inheritdoc />
        public override string Name => "NotifySync";

        /// <inheritdoc />
        public override Guid Id => Guid.Parse("f8b2d3e1-4c5d-6e7f-8a9b-0c1d2e3f4a5b");

        /// <summary>
        /// Gets the notification manager.
        /// </summary>
        public NotificationManager? NotificationManager => _notificationManager;

        /// <inheritdoc />
        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = "NotifySync",
                    EmbeddedResourcePath = GetType().Namespace + ".ConfigurationPage.html"
                }
            };
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _notificationManager?.Dispose();
            _notificationManager = null;
            GC.SuppressFinalize(this);
        }
    }
}