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
    /// Makes the plugin's client script run inside Jellyfin Web.
    ///
    /// <para><b>Why index.html injection (Option A) for Jellyfin 10.11.11:</b> Jellyfin 10.11 still
    /// serves <c>jellyfin-web</c> as static files from <see cref="IServerApplicationPaths.WebPath"/>
    /// and exposes no supported server-side hook to add a script to the client. The alternatives
    /// are: (B) depend on the third-party <c>File Transformation</c> plugin — rejected, it adds an
    /// external hard dependency and a second failure point; (C) a custom branding/CSS injection —
    /// insufficient, CSS cannot run logic. Controlled, marker-based editing of <c>index.html</c> is
    /// what the mainstream UI plugins (Jellyscrub, Home Screen Sections, ...) do on 10.10 and 10.11.</para>
    ///
    /// <para>The edit is a single line before <c>&lt;/body&gt;</c>, tagged with
    /// <see cref="Marker"/>: idempotent (never inserted twice), reversible (removed on
    /// <see cref="StopAsync"/> = plugin stop/uninstall, matching only our marker), and it never
    /// touches anything else in the file. A Jellyfin Web update regenerates a clean
    /// <c>index.html</c>; this service re-applies the line on the next server start. We never keep
    /// or restore an old copy of the file.</para>
    /// </summary>
    public class ScriptInjectionHostedService : IHostedService
    {
        internal const string Marker = "startup-ads-inject";

        private const string ScriptTag =
            "<script id=\"" + Marker + "\" src=\"StartupAds/ClientScript\" defer></script>";

        private static readonly Regex _tagPattern = new(
            @"\s*<script\s+id=""" + Marker + @"""[^>]*>\s*</script>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
            _logger.LogInformation("[StartupAds] Plugin starting; ensuring client script injection.");
            SafeRun(Apply);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            SafeRun(Remove);
            return Task.CompletedTask;
        }

        private void SafeRun(Action action)
        {
            try
            {
                action();
            }
            catch (UnauthorizedAccessException)
            {
                _logger.LogWarning(
                    "[StartupAds] No write permission on jellyfin-web/index.html. The overlay will "
                    + "not load until the Jellyfin process can write to {WebPath}. Jellyfin itself is unaffected.",
                    _paths.WebPath);
            }
            catch (Exception ex)
            {
                // A plugin must never break Jellyfin startup/shutdown.
                _logger.LogError(ex, "[StartupAds] Client script injection step failed (non-fatal).");
            }
        }

        private string? IndexPath()
        {
            var webRoot = _paths.WebPath;
            if (string.IsNullOrEmpty(webRoot) || !Directory.Exists(webRoot))
            {
                _logger.LogWarning(
                    "[StartupAds] jellyfin-web path '{WebPath}' not found; client script not injected "
                    + "(expected on API-only / headless deployments).",
                    webRoot);
                return null;
            }

            var index = Path.Combine(webRoot, "index.html");
            if (!System.IO.File.Exists(index))
            {
                _logger.LogWarning("[StartupAds] {Index} does not exist; client script not injected.", index);
                return null;
            }

            return index;
        }

        private void Apply()
        {
            var index = IndexPath();
            if (index is null)
            {
                return;
            }

            var html = ReadAllText(index);

            if (html.Contains(Marker, StringComparison.Ordinal))
            {
                _logger.LogDebug("[StartupAds] Client script already present in index.html.");
                return;
            }

            string updated;
            var bodyClose = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            updated = bodyClose >= 0
                ? html[..bodyClose] + ScriptTag + "\n" + html[bodyClose..]
                : html + "\n" + ScriptTag + "\n";

            WriteAllText(index, updated);
            _logger.LogInformation("[StartupAds] Client script injected into {Index}.", index);
        }

        private void Remove()
        {
            var index = IndexPath();
            if (index is null)
            {
                return;
            }

            var html = ReadAllText(index);
            if (!html.Contains(Marker, StringComparison.Ordinal))
            {
                return;
            }

            var cleaned = _tagPattern.Replace(html, string.Empty);
            WriteAllText(index, cleaned);
            _logger.LogInformation("[StartupAds] Client script injection removed from index.html.");
        }

        private static string ReadAllText(string path)
        {
            // Small retry for the rare case Jellyfin Web is being written concurrently.
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    return System.IO.File.ReadAllText(path, Encoding.UTF8);
                }
                catch (IOException) when (attempt < 3)
                {
                    Thread.Sleep(150);
                }
            }
        }

        private static void WriteAllText(string path, string content)
        {
            var info = new FileInfo(path);
            var wasReadOnly = info.Exists && info.IsReadOnly;
            if (wasReadOnly)
            {
                info.IsReadOnly = false;
            }

            try
            {
                for (var attempt = 0; ; attempt++)
                {
                    try
                    {
                        System.IO.File.WriteAllText(path, content, new UTF8Encoding(false));
                        return;
                    }
                    catch (IOException) when (attempt < 3)
                    {
                        Thread.Sleep(150);
                    }
                }
            }
            finally
            {
                if (wasReadOnly)
                {
                    try
                    {
                        new FileInfo(path).IsReadOnly = true;
                    }
                    catch
                    {
                        // best effort
                    }
                }
            }
        }
    }
}
