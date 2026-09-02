using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.StartupAds.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StartupAds
{
    /// <summary>
    /// Entry point for the Jellyfin Startup Ads plugin.
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public Plugin(
            IApplicationPaths applicationPaths,
            IXmlSerializer xmlSerializer,
            ILogger<Plugin> logger)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;

            // Emitted once at load so the server log shows exactly what Jellyfin resolved.
            logger.LogInformation(
                "[StartupAds] Plugin loaded. Name='{Name}' Id={Id} Version={Version}",
                Name,
                Id,
                Version);
        }

        public static Plugin? Instance { get; private set; }

        public override string Name => "Jellyfin Startup Ads";

        public override Guid Id => Guid.Parse("6d1a9b6e-6b3e-4f9a-9c2d-3a7f1e2c4b90");

        public override string Description =>
            "Muestra automáticamente anuncios multimedia (imagen, vídeo o texto) al abrir Jellyfin Web.";

        public IEnumerable<PluginPageInfo> GetPages()
        {
            var prefix = GetType().Namespace;
            return new[]
            {
                new PluginPageInfo
                {
                    // "Name" is the route key (configurationpage?name=startupads); keep it
                    // spaceless and stable. "DisplayName" is what the dashboard shows.
                    Name = "startupads",
                    DisplayName = Name,
                    EmbeddedResourcePath = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}.Configuration.configPage.html",
                        prefix)
                }
            };
        }
    }
}
