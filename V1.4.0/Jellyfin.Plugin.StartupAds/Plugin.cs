using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.StartupAds.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.StartupAds
{
    /// <summary>
    /// Entry point for the Jellyfin Startup Ads plugin. Constructor signature matches the
    /// official Jellyfin plugin template exactly (2 parameters) so plugin loading is reliable.
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public static Plugin? Instance { get; private set; }

        public override string Name => "Jellyfin Startup Ads";

        public override Guid Id => Guid.Parse("6d1a9b6e-6b3e-4f9a-9c2d-3a7f1e2c4b90");

        public override string Description =>
            "Muestra automáticamente anuncios multimedia (imagen, vídeo o texto) al abrir Jellyfin Web.";

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = Name,
                    EmbeddedResourcePath = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}.Configuration.configPage.html",
                        GetType().Namespace),
                    EnableInMainMenu = true,
                    MenuSection = "server"
                }
            };
        }
    }
}
