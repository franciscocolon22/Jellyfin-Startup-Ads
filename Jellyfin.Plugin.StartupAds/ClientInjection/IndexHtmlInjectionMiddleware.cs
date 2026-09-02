using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Plugin.StartupAds.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StartupAds.ClientInjection
{
    /// <summary>
    /// Intercepts responses for <c>jellyfin-web</c>'s <c>index.html</c> and injects the plugin's
    /// client <c>&lt;script&gt;</c> into the HTML <b>in memory</b>. Nothing is written to disk, so:
    /// <list type="bullet">
    ///   <item>a Jellyfin Web update cannot undo it;</item>
    ///   <item>it works when <c>web/</c> is read-only or root-owned (typical Docker image);</item>
    ///   <item>uninstalling the plugin removes the behaviour immediately, with no cleanup step.</item>
    /// </list>
    /// Registered via <see cref="StartupAdsStartupFilter"/> (ASP.NET Core <c>IStartupFilter</c>),
    /// so it runs before Jellyfin's static-file middleware and can buffer + rewrite the body.
    /// </summary>
    public sealed class IndexHtmlInjectionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<IndexHtmlInjectionMiddleware> _logger;
        private long _injectedCount;
        private bool _loggedFirstInjection;

        public IndexHtmlInjectionMiddleware(RequestDelegate next, ILogger<IndexHtmlInjectionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        private static PluginConfiguration Config =>
            Plugin.Instance?.Configuration ?? new PluginConfiguration();

        public async Task Invoke(HttpContext context)
        {
            if (!ShouldConsider(context))
            {
                await _next(context).ConfigureAwait(false);
                return;
            }

            // Force an uncompressed response so we can read and rewrite the HTML. index.html is a
            // few KB; the size cost is negligible and only applies to this one document.
            context.Request.Headers.Remove("Accept-Encoding");

            var originalBody = context.Response.Body;
            using var buffer = new MemoryStream();
            context.Response.Body = buffer;

            try
            {
                await _next(context).ConfigureAwait(false);

                buffer.Seek(0, SeekOrigin.Begin);
                var contentType = context.Response.ContentType ?? string.Empty;
                var isHtml = context.Response.StatusCode == StatusCodes.Status200OK
                             && contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase)
                             && string.IsNullOrEmpty(context.Response.Headers.ContentEncoding);

                if (!isHtml)
                {
                    context.Response.Body = originalBody;
                    await buffer.CopyToAsync(originalBody).ConfigureAwait(false);
                    return;
                }

                var html = await new StreamReader(buffer, Encoding.UTF8).ReadToEndAsync().ConfigureAwait(false);
                var patched = Config.InjectClientScript ? IndexHtmlInjector.Inject(html) : html;

                var bytes = Encoding.UTF8.GetBytes(patched);
                context.Response.Body = originalBody;
                context.Response.ContentLength = bytes.Length;
                await context.Response.Body.WriteAsync(bytes).ConfigureAwait(false);

                if (!ReferenceEquals(patched, html))
                {
                    _injectedCount++;
                    if (!_loggedFirstInjection)
                    {
                        _loggedFirstInjection = true;
                        _logger.LogInformation(
                            "[StartupAds] Client script injected into index.html response ({Path}).",
                            context.Request.Path.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                // Never break Jellyfin Web because of this middleware.
                _logger.LogError(ex, "[StartupAds] index.html injection failed; serving the original response.");
                context.Response.Body = originalBody;
                if (!context.Response.HasStarted && buffer.Length > 0)
                {
                    buffer.Seek(0, SeekOrigin.Begin);
                    await buffer.CopyToAsync(originalBody).ConfigureAwait(false);
                }
            }
            finally
            {
                context.Response.Body = originalBody;
            }
        }

        private static bool ShouldConsider(HttpContext context)
        {
            if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
            {
                return false;
            }

            var path = context.Request.Path.Value ?? string.Empty;
            var basePath = Config.WebBasePath;
            if (string.IsNullOrWhiteSpace(basePath))
            {
                basePath = "/web";
            }

            basePath = "/" + basePath.Trim('/');

            return path.EndsWith("/index.html", StringComparison.OrdinalIgnoreCase)
                   || path.Equals(basePath, StringComparison.OrdinalIgnoreCase)
                   || path.Equals(basePath + "/", StringComparison.OrdinalIgnoreCase)
                   || path == "/";
        }
    }
}
