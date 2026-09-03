using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StartupAds.ClientInjection
{
    /// <summary>
    /// Inserts <see cref="IndexHtmlInjectionMiddleware"/> at the very front of the ASP.NET Core
    /// request pipeline. <c>IStartupFilter</c> is a first-class ASP.NET Core extension point that
    /// Jellyfin's generic host honours for any service registered in DI — it is not a Jellyfin
    /// plugin API, and Jellyfin core does not need to know about it.
    ///
    /// Running first means the middleware wraps the response stream before Jellyfin's static-file
    /// middleware produces index.html, which is what lets it rewrite the body in memory.
    /// </summary>
    public sealed class StartupAdsStartupFilter : IStartupFilter
    {
        private readonly ILogger<StartupAdsStartupFilter> _logger;

        public StartupAdsStartupFilter(ILogger<StartupAdsStartupFilter> logger)
        {
            _logger = logger;
        }

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                try
                {
                    app.UseMiddleware<IndexHtmlInjectionMiddleware>();
                    _logger.LogInformation(
                        "[StartupAds] index.html injection middleware registered (in-memory, no disk changes).");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[StartupAds] Failed to register injection middleware; the overlay will not load.");
                }

                next(app);
            };
        }
    }
}
