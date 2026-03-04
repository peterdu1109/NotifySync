using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace NotifySync
{
    /// <summary>
    /// Registers NotifySync services into the Jellyfin DI container.
    /// </summary>
    public class NotifySyncServiceRegistrator : IPluginServiceRegistrator
    {
        /// <inheritdoc />
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            serviceCollection.AddHostedService<NotifySyncEntryPoint>();
        }
    }
}
