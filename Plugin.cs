using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using NotifySync.Configuration;

namespace NotifySync
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public override string Name => "NotifySync";
        public override Guid Id => Guid.Parse("95655672-2342-4321-8291-321312312312");
        
        public static Plugin? Instance { get; private set; }

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[] { new PluginPageInfo { Name = this.Name, EmbeddedResourcePath = GetType().Namespace + ".ConfigurationPage.html" } };
        }
    }
}