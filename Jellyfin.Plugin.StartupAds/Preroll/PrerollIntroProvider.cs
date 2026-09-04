using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.StartupAds.Configuration;
using Jellyfin.Plugin.StartupAds.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StartupAds.Preroll
{
    /// <summary>
    /// Jellyfin discovers every <see cref="IIntroProvider"/> in a plugin assembly automatically
    /// (<c>ApplicationHost.GetExports&lt;IIntroProvider&gt;()</c>). When any client starts playing a
    /// movie or episode, Jellyfin asks this provider for intro videos and the client plays them as
    /// a pre-roll — so this works on native apps (Android, Android TV, Roku…), not just the web.
    ///
    /// Jellyfin 10.11 only accepts an intro that resolves to a video <b>already in a library</b>
    /// (see <c>LibraryManager.ResolveIntro</c>), so a pre-roll ad references a Jellyfin item id,
    /// not a loose file.
    ///
    /// A pre-roll video the current viewer cannot access (their user account has no permission
    /// on the library that holds it) is skipped rather than handed to the client: Jellyfin would
    /// otherwise return an intro item with no playable media source, which fails the ENTIRE
    /// playback ("No ha sido posible encontrar un medio válido para reproducir") instead of just
    /// the pre-roll. See <see cref="Video.IsVisibleStandalone"/>.
    /// </summary>
    public sealed class PrerollIntroProvider : IIntroProvider
    {
        private readonly ILogger<PrerollIntroProvider> _logger;
        private readonly ILibraryManager _libraryManager;

        public PrerollIntroProvider(ILoggerFactory loggerFactory, ILibraryManager libraryManager)
        {
            _logger = loggerFactory.CreateLogger<PrerollIntroProvider>();
            _libraryManager = libraryManager;
        }

        public string Name => "Jellyfin Startup Ads";

        public Task<IEnumerable<IntroInfo>> GetIntros(BaseItem item, User user)
        {
            try
            {
                var cfg = Plugin.Instance?.Configuration.Preroll;
                if (cfg is null || !cfg.Enabled || cfg.Advertisements.Count == 0)
                {
                    return Empty;
                }

                var kind = item.GetBaseItemKind();
                var applies = cfg.AppliesTo switch
                {
                    PrerollAppliesTo.Movies => kind == BaseItemKind.Movie,
                    PrerollAppliesTo.Episodes => kind == BaseItemKind.Episode,
                    _ => kind is BaseItemKind.Movie or BaseItemKind.Episode
                };

                if (!applies)
                {
                    return Empty;
                }

                if (cfg.Frequency == PrerollFrequency.RandomChance
                    && Random.Shared.Next(100) >= Math.Clamp(cfg.RandomChancePercent, 0, 100))
                {
                    return Empty;
                }

                var userId = user.Id.ToString();
                var now = DateTime.Now;

                if (cfg.Frequency == PrerollFrequency.OncePerDay
                    && cfg.ShownLog.Any(s =>
                        string.Equals(s.UserId, userId, StringComparison.OrdinalIgnoreCase)
                        && s.Date.Date == now.Date))
                {
                    return Empty;
                }

                var chosen = PrerollManager.Select(cfg, userId, now);
                if (chosen.Count == 0)
                {
                    return Empty;
                }

                var infos = new List<IntroInfo>();
                foreach (var a in chosen)
                {
                    if (!Guid.TryParse(a.ItemId, out var g) || g == Guid.Empty)
                    {
                        continue;
                    }

                    // The intro must resolve to a video still present in a library; skip stale ids.
                    if (_libraryManager.GetItemById(g) is not Video video || string.IsNullOrEmpty(video.Path))
                    {
                        _logger.LogWarning("[StartupAds] Pre-roll '{Name}': el vídeo ya no está en la biblioteca.", a.Name);
                        continue;
                    }

                    // The viewer must actually be able to see/play this item — otherwise Jellyfin
                    // returns it with no playable media source and the WHOLE playback (movie
                    // included) fails with "no valid media found". Never hand out an intro the
                    // current user cannot access; skip it and let the movie play with no pre-roll.
                    if (!video.IsVisibleStandalone(user))
                    {
                        _logger.LogWarning(
                            "[StartupAds] Pre-roll '{Name}' omitido para {User}: sin acceso a la biblioteca que contiene el vídeo (revisa los permisos de biblioteca del usuario).",
                            a.Name,
                            user.Username);
                        continue;
                    }

                    // A library item that has not finished being probed/scanned has no playable
                    // media source yet; including it produces the same "no valid media" error.
                    if (video.MediaSourceCount <= 0)
                    {
                        _logger.LogWarning(
                            "[StartupAds] Pre-roll '{Name}' omitido: el vídeo no tiene ninguna fuente de medios reproducible (¿aún sin escanear?).",
                            a.Name);
                        continue;
                    }

                    infos.Add(new IntroInfo { ItemId = g, Path = video.Path });
                }

                if (infos.Count == 0)
                {
                    return Empty;
                }

                if (cfg.Frequency == PrerollFrequency.OncePerDay && Plugin.Instance is { } p)
                {
                    cfg.ShownLog.RemoveAll(s => s.Date.Date < now.Date.AddDays(-14));
                    cfg.ShownLog.Add(new PrerollShown { UserId = userId, Date = now });
                    p.SaveConfiguration();
                }

                _logger.LogInformation(
                    "[StartupAds] Pre-roll: {Count} vídeo(s) antes de '{Item}' para {User}.",
                    infos.Count,
                    item.Name,
                    user.Username);

                return Task.FromResult<IEnumerable<IntroInfo>>(infos);
            }
            catch (Exception ex)
            {
                // A plugin must never break playback.
                _logger.LogError(ex, "[StartupAds] Pre-roll selection failed.");
                return Empty;
            }
        }

        private static Task<IEnumerable<IntroInfo>> Empty
            => Task.FromResult(Enumerable.Empty<IntroInfo>());
    }
}
