using Jellyfin.Plugin.StartupAds.ClientInjection;
using Jellyfin.Plugin.StartupAds.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
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
            serviceCollection.AddSingleton<PrerollManager>();
            // PrerollIntroProvider is auto-discovered by Jellyfin (GetExports<IIntroProvider>()).

            // In-memory injection of the client <script> into jellyfin-web/index.html responses.
            // Uses the standard ASP.NET Core IStartupFilter extension point (not a Jellyfin API):
            // it never writes to disk, so it survives Jellyfin Web updates and works on read-only
            // / Docker deployments. See ClientInjection/StartupAdsStartupFilter.
            serviceCollection.AddSingleton<IStartupFilter, StartupAdsStartupFilter>();
        }
    }
}
