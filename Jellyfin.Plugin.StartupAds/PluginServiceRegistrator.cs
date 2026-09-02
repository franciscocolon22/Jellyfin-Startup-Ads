using Jellyfin.Plugin.StartupAds.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.StartupAds
{
    /// <summary>
    /// Registers plugin services into the Jellyfin host container.
    /// </summary>
    public class PluginServiceRegistrator : IPluginServiceRegistrator
    {
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            serviceCollection.AddSingleton<MediaFileService>();
            serviceCollection.AddSingleton<AdvertisementManager>();
            serviceCollection.AddHostedService<ScriptInjectionHostedService>();
        }
    }
}
