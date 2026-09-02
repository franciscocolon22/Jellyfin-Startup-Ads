using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StartupAds.Services
{
    /// <summary>
    /// Injects a single &lt;script&gt; tag into Jellyfin Web's <c>index.html</c> so the overlay
    /// bootstrap runs on every client load. This is the mechanism used by most UI-modifying
    /// Jellyfin plugins today (Jellyscrub, Home Screen Sections, ...) because Jellyfin has no
    /// official client-injection API in 10.10/10.11.
    ///
    /// The edit is idempotent, reversible and re-applied on every server start (Jellyfin Web
    /// updates overwrite index.html). Removing the plugin + one Jellyfin restart followed by a
    /// web update restores the original file; <see cref="RemoveInjection"/> also runs on shutdown.
    /// </summary>
    public class ScriptInjectionHostedService : IHostedService
    {
        private const string Marker = "startup-ads-inject";
        private const string ScriptTag =
            "<script id=\"" + Marker + "\" src=\"StartupAds/ClientScript\" defer></script>";

        private readonly ILogger<ScriptInjectionHostedService> _logger;
        private readonly IServerApplicationPaths _paths;

        public ScriptInjectionHostedService(
            ILogger<ScriptInjectionHostedService> logger,
            IServerApplicationPaths paths)
        {
            _logger = logger;
            _paths = paths;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                Apply();
            }
            catch (Exception ex)
            {
                // Never let an injection failure stop Jellyfin from starting.
                _logger.LogError(ex, "[StartupAds] Failed to inject client script.");
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                RemoveInjection();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[StartupAds] Failed to clean up client script injection.");
            }

            return Task.CompletedTask;
        }

        private string? IndexPath()
        {
            var webRoot = _paths.WebPath;
            if (string.IsNullOrEmpty(webRoot) || !Directory.Exists(webRoot))
            {
                _logger.LogWarning("[StartupAds] Web path not found ({Path}); client script not injected. "
                                   + "This is expected on headless/API-only deployments.", webRoot);
                return null;
            }

            var index = Path.Combine(webRoot, "index.html");
            return File.Exists(index) ? index : null;
        }

        private void Apply()
        {
            var index = IndexPath();
            if (index is null)
            {
                return;
            }

            var html = File.ReadAllText(index, Encoding.UTF8);

            if (html.Contains(Marker, StringComparison.Ordinal))
            {
                _logger.LogDebug("[StartupAds] Client script already present in index.html.");
                return;
            }

            string updated;
            var bodyClose = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            if (bodyClose >= 0)
            {
                updated = html[..bodyClose] + ScriptTag + "\n" + html[bodyClose..];
            }
            else
            {
                updated = html + "\n" + ScriptTag;
            }

            File.WriteAllText(index, updated, new UTF8Encoding(false));
            _logger.LogInformation("[StartupAds] Client script injected into {Index}.", index);
        }

        private void RemoveInjection()
        {
            var index = IndexPath();
            if (index is null)
            {
                return;
            }

            var html = File.ReadAllText(index, Encoding.UTF8);
            if (!html.Contains(Marker, StringComparison.Ordinal))
            {
                return;
            }

            var cleaned = Regex.Replace(
                html,
                @"\s*<script id=""" + Marker + @"""[^>]*></script>",
                string.Empty,
                RegexOptions.IgnoreCase);

            File.WriteAllText(index, cleaned, new UTF8Encoding(false));
            _logger.LogInformation("[StartupAds] Client script injection removed from index.html.");
        }
    }
}
