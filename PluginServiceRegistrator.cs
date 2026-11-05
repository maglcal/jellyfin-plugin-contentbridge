using Jellyfin.Plugin.ContentBridge.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.ContentBridge
{
    /// <summary>
    /// Registers plugin services in the Jellyfin DI container.
    /// </summary>
    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        /// <inheritdoc />
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            serviceCollection.AddSingleton<ContentApiClient>();
            serviceCollection.AddSingleton<StrmLibraryWriter>();
            serviceCollection.AddSingleton<StreamProxyService>();
            serviceCollection.AddSingleton<SyncService>();
            serviceCollection.AddSingleton<MediaBrowser.Model.Tasks.IScheduledTask, Tasks.SyncContentTask>();
        }
    }
}
