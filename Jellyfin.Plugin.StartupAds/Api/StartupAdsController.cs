using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using Jellyfin.Plugin.StartupAds.Configuration;
using Jellyfin.Plugin.StartupAds.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StartupAds.Api
{
    /// <summary>
    /// HTTP endpoints for the Startup Ads plugin.
    /// <list type="bullet">
    ///   <item><b>Anonymous</b> (<c>[AllowAnonymous]</c>): only <c>ClientScript</c> / <c>ClientStyle</c>
    ///         (static assets, identical for every viewer).</item>
    ///   <item><b>Authenticated user</b> (<c>[Authorize]</c> → Jellyfin's default policy): <c>Config</c>,
    ///         <c>Media</c>, <c>Media/Background</c>, <c>Track</c>. NOTE: Jellyfin 10.11 removed the named
    ///         <c>"DefaultAuthorization"</c> policy — a bare <c>[Authorize]</c> uses
    ///         <c>AuthorizationOptions.DefaultPolicy</c> (an authenticated Jellyfin user).</item>
    ///   <item><b>Administrator</b> (<c>[Authorize(Policy = "RequiresElevation")]</c>): everything under <c>Admin/</c>.</item>
    /// </list>
    /// </summary>
    [ApiController]
    [Route("StartupAds")]
    public class StartupAdsController : ControllerBase
    {
        // Jellyfin puts the internal user id in this claim.
        private const string UserIdClaim = "Jellyfin-UserId";

        private readonly ILogger<StartupAdsController> _logger;
        private readonly AdvertisementManager _manager;
        private readonly MediaFileService _files;
        private readonly ILibraryManager _libraryManager;

        public StartupAdsController(
            ILogger<StartupAdsController> logger,
            AdvertisementManager manager,
            MediaFileService files,
            ILibraryManager libraryManager)
        {
            _logger = logger;
            _manager = manager;
            _files = files;
            _libraryManager = libraryManager;
        }

        private static PluginConfiguration Config =>
            Plugin.Instance?.Configuration ?? new PluginConfiguration();

        private Guid CurrentUserId()
        {
            var raw = User.FindFirstValue(UserIdClaim)
                      ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
        }

        // ---------------------------------------------------------------------
        // Public assets
        // ---------------------------------------------------------------------
        [HttpGet("ClientScript")]
        [AllowAnonymous]
        [Produces("application/javascript")]
        public ActionResult GetClientScript()
        {
            var js = ReadEmbedded("Jellyfin.Plugin.StartupAds.Web.startup-ads.js");
            if (js is null)
            {
                return NotFound();
            }

            Response.Headers.CacheControl = "public, max-age=3600";
            return Content(js, "application/javascript");
        }

        [HttpGet("ClientStyle")]
        [AllowAnonymous]
        [Produces("text/css")]
        public ActionResult GetClientStyle()
        {
            var css = ReadEmbedded("Jellyfin.Plugin.StartupAds.Web.startup-ads.css");
            if (css is null)
            {
                return NotFound();
            }

            Response.Headers.CacheControl = "public, max-age=3600";
            return Content(css, "text/css");
        }

        // ---------------------------------------------------------------------
        // Authenticated user API
        // ---------------------------------------------------------------------
        [HttpGet("Config")]
        [Authorize]
        public ActionResult<ClientBootstrapDto> GetConfig()
        {
            var cfg = Config;
            Response.Headers.CacheControl = "private, no-store";

            ClientBootstrapDto dto;
            try
            {
                dto = BuildBootstrap(cfg, previewMode: false);

                if (dto.Enabled)
                {
                    foreach (var ad in _manager.GetActiveForUser(CurrentUserId(), DateTime.Now))
                    {
                        dto.Ads.Add(ToClientDto(ad, cfg));
                    }
                }
            }
            catch (Exception ex)
            {
                // Fail safe: a plugin error must never break Jellyfin Web. Return "no ads".
                _logger.LogError(ex, "[StartupAds] Failed to build client config; returning an empty response.");
                return new ClientBootstrapDto { Enabled = false };
            }

            return dto;
        }

        [HttpGet("Media/{adId}")]
        [Authorize]
        public ActionResult GetMedia([FromRoute] Guid adId)
        {
            var check = ResolveAccessibleAd(adId, out var ad);
            if (check is not null)
            {
                return check;
            }

            var path = _files.ResolveFile(Config.AdsDirectory, ad!.MediaFile);
            if (path is null)
            {
                return NotFound();
            }

            Response.Headers.CacheControl = "private, max-age=0, must-revalidate";
            return PhysicalFile(path, MediaFileService.ContentTypeFor(path), enableRangeProcessing: true);
        }

        [HttpGet("Media/{adId}/Background")]
        [Authorize]
        public ActionResult GetBackground([FromRoute] Guid adId)
        {
            var check = ResolveAccessibleAd(adId, out var ad);
            if (check is not null)
            {
                return check;
            }

            if (string.IsNullOrEmpty(ad!.BackgroundFile))
            {
                return NotFound();
            }

            var path = _files.ResolveFile(Config.AdsDirectory, ad.BackgroundFile);
            if (path is null)
            {
                return NotFound();
            }

            Response.Headers.CacheControl = "private, max-age=0, must-revalidate";
            return PhysicalFile(path, MediaFileService.ContentTypeFor(path), enableRangeProcessing: true);
        }

        [HttpPost("Track/{adId}/{kind}")]
        [Authorize]
        public ActionResult Track([FromRoute] Guid adId, [FromRoute] string kind)
        {
            if (!AdvertisementManager.IsValidTrackingEvent(kind))
            {
                return BadRequest($"Unknown tracking event '{kind}'.");
            }

            var check = ResolveAccessibleAd(adId, out _);
            if (check is not null)
            {
                return check;
            }

            _manager.Track(adId, kind);
            return NoContent();
        }

        // ---------------------------------------------------------------------
        // Admin API
        // ---------------------------------------------------------------------
        [HttpGet("Admin/Configuration")]
        [Authorize(Policy = "RequiresElevation")]
        public ActionResult<PluginConfiguration> GetAdminConfig() => Config;

        [HttpPost("Admin/Configuration")]
        [Authorize(Policy = "RequiresElevation")]
        public ActionResult SaveAdminConfig([FromBody] PluginConfiguration incoming)
        {
            if (Plugin.Instance is not { } p)
            {
                return StatusCode(500);
            }

            // These lists are managed through their own endpoints.
            incoming.Advertisements = p.Configuration.Advertisements;
            incoming.Statistics = p.Configuration.Statistics;

            incoming.DefaultDurationSeconds = Math.Clamp(incoming.DefaultDurationSeconds, 1, 600);
            incoming.SkipAfterSeconds = Math.Clamp(incoming.SkipAfterSeconds, 0, 600);
            incoming.MaxAdsPerStartup = Math.Clamp(incoming.MaxAdsPerStartup, 1, 20);
            incoming.OverlayOpacity = Math.Clamp(incoming.OverlayOpacity, 0d, 1d);
            incoming.MaxWidthPx = Math.Clamp(incoming.MaxWidthPx, 200, 6000);
            incoming.MaxHeightPx = Math.Clamp(incoming.MaxHeightPx, 200, 6000);
            incoming.BorderRadiusPx = Math.Clamp(incoming.BorderRadiusPx, 0, 80);
            incoming.ObjectFit = incoming.ObjectFit == "cover" ? "cover" : "contain";
            if (string.IsNullOrWhiteSpace(incoming.AccentColor)
                || !System.Text.RegularExpressions.Regex.IsMatch(incoming.AccentColor, "^#[0-9A-Fa-f]{3,8}$"))
            {
                incoming.AccentColor = "#00a4dc";
            }

            incoming.Language = incoming.Language == "en" ? "en" : "es";
            incoming.WebBasePath = string.IsNullOrWhiteSpace(incoming.WebBasePath)
                ? "/web"
                : "/" + incoming.WebBasePath.Trim().Trim('/');

            p.UpdateConfiguration(incoming);
            _logger.LogInformation("[StartupAds] Configuration saved by administrator.");
            return NoContent();
        }

        [HttpGet("Admin/Advertisements")]
        [Authorize(Policy = "RequiresElevation")]
        public ActionResult<IReadOnlyList<Advertisement>> GetAds() => Ok(_manager.GetAll());

        [HttpPost("Admin/Advertisements")]
        [Authorize(Policy = "RequiresElevation")]
        public ActionResult<Advertisement> CreateAd([FromBody] Advertisement ad)
        {
            if (!TryNormalize(ad, out var error))
            {
                return BadRequest(error);
            }

            return Ok(_manager.Create(ad));
        }

        [HttpPost("Admin/Advertisements/{id}")]
        [Authorize(Policy = "RequiresElevation")]
        public ActionResult<Advertisement> UpdateAd([FromRoute] Guid id, [FromBody] Advertisement ad)
        {
            ad.Id = id;
            if (!TryNormalize(ad, out var error))
            {
                return BadRequest(error);
            }

            var updated = _manager.Update(ad);
            return updated is null ? NotFound() : Ok(updated);
        }

        [HttpDelete("Admin/Advertisements/{id}")]
        [Authorize(Policy = "RequiresElevation")]
        public ActionResult DeleteAd([FromRoute] Guid id)
            => _manager.Delete(id) ? NoContent() : NotFound();

        [HttpPost("Admin/Advertisements/{id}/Duplicate")]
        [Authorize(Policy = "RequiresElevation")]
        public ActionResult<Advertisement> DuplicateAd([FromRoute] Guid id)
        {
            var clone = _manager.Duplicate(id);
            return clone is null ? NotFound() : Ok(clone);
        }

        [HttpPost("Admin/Advertisements/{id}/Enabled/{value}")]
        [Authorize(Policy = "RequiresElevation")]
        public ActionResult SetEnabled([FromRoute] Guid id, [FromRoute] bool value)
            => _manager.SetEnabled(id, value) ? NoContent() : NotFound();

        [HttpPost("Admin/ValidatePath")]
        [Authorize(Policy = "RequiresElevation")]
        public ActionResult<PathValidationResult> ValidatePath([FromBody] ValidatePathRequest req)
            => Ok(_files.ValidateDirectory(req.Path));

        [HttpGet("Admin/Files")]
        [Authorize(Policy = "RequiresElevation")]
        public ActionResult<IReadOnlyList<MediaFileInfo>> GetFiles()
            => Ok(_files.ListFiles(Config.AdsDirectory));

        [HttpPost("Admin/Scan")]
        [Authorize(Policy = "RequiresElevation")]
        public ActionResult<AdvertisementManager.ScanResult> Scan() => Ok(_manager.ScanAndImport());

        [HttpGet("Admin/Preview")]
        [Authorize(Policy = "RequiresElevation")]
        public ActionResult<ClientBootstrapDto> Preview([FromQuery] Guid? adId)
        {
            var cfg = Config;
            var dto = BuildBootstrap(cfg, previewMode: true);

            var ads = adId is { } gid && _manager.Get(gid) is { } single
                ? new List<Advertisement> { single }
                : _manager.GetAll().Where(a => a.Enabled).Take(1).ToList();

            foreach (var ad in ads)
            {
                dto.Ads.Add(ToClientDto(ad, cfg));
            }

            return dto;
        }

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------

        /// <summary>
        /// Applies the full access policy for a media/track request: authenticated (enforced by the
        /// attribute), ad exists, enabled, <c>ShowOnStartup</c>, and the current user is targeted.
        /// Returns a non-null <see cref="ActionResult"/> to short-circuit, or null when access is granted.
        /// </summary>
        private ActionResult? ResolveAccessibleAd(Guid adId, out Advertisement? ad)
        {
            ad = _manager.Get(adId);
            if (ad is null)
            {
                return NotFound();
            }

            if (!ad.Enabled || !ad.ShowOnStartup)
            {
                return NotFound();
            }

            if (!AdvertisementManager.IsUserTargeted(ad, CurrentUserId()))
            {
                _logger.LogWarning("[StartupAds] User {User} denied access to ad {Ad}.", CurrentUserId(), adId);
                return Forbid();
            }

            return null;
        }

        private static ClientBootstrapDto BuildBootstrap(PluginConfiguration cfg, bool previewMode) => new()
        {
            Enabled = previewMode || (cfg.Enabled && cfg.ShowOnStartup),
            DisplayMode = cfg.DisplayMode.ToString(),
            FrequencyMode = previewMode ? "EveryStartup" : cfg.FrequencyMode.ToString(),
            ShowCountdown = cfg.ShowCountdown,
            DefaultDurationSeconds = cfg.DefaultDurationSeconds,
            SkipButtonMode = cfg.SkipButtonMode.ToString(),
            ShowCloseButton = previewMode || cfg.ShowCloseButton,
            AllowCloseWithEscape = previewMode || cfg.AllowCloseWithEscape,
            AutoplayVideo = cfg.AutoplayVideo,
            MutedVideo = cfg.MutedVideo,
            LoopVideo = cfg.LoopVideo,
            ShowVideoControls = cfg.ShowVideoControls,
            OverlayOpacity = cfg.OverlayOpacity,
            MaxWidthPx = cfg.MaxWidthPx,
            MaxHeightPx = cfg.MaxHeightPx,
            BorderRadiusPx = cfg.BorderRadiusPx,
            AccentColor = cfg.AccentColor,
            Language = cfg.Language,
            StatisticsEnabled = !previewMode && cfg.EnableStatistics
        };

        private static ClientAdDto ToClientDto(Advertisement ad, PluginConfiguration cfg)
        {
            var hasMedia = !string.IsNullOrEmpty(ad.MediaFile)
                           && ad.Type != AdvertisementType.Text;
            var mediaKind = hasMedia
                ? MediaFileService.TypeFor(ad.MediaFile) switch
                {
                    AdvertisementType.Video => "video",
                    _ => "image"
                }
                : null;
            return new ClientAdDto
            {
                Id = ad.Id.ToString(),
                Type = ad.Type.ToString(),
                Title = ad.Title ?? string.Empty,
                Description = ad.Description ?? string.Empty,
                MediaUrl = hasMedia ? $"StartupAds/Media/{ad.Id}" : null,
                MediaKind = mediaKind,
                BackgroundUrl = string.IsNullOrEmpty(ad.BackgroundFile)
                    ? null
                    : $"StartupAds/Media/{ad.Id}/Background",
                ObjectFit = ad.ObjectFit == "cover" ? "cover" : "contain",
                // The countdown is a single global value ("Duración del anuncio"), identical for
                // every ad type. There is no per-ad duration and no "use the video length".
                DurationSeconds = cfg.DefaultDurationSeconds,
                UseVideoDuration = false,
                AllowSkip = ad.AllowSkip && cfg.AllowSkip,
                SkipAfterSeconds = cfg.DefaultDurationSeconds,
                ShowCountdown = ad.ShowCountdown && cfg.ShowCountdown,
                ButtonText = ad.ButtonText ?? string.Empty,
                ButtonAction = ad.ButtonAction.ToString(),
                ButtonUrl = ad.ButtonAction == AdButtonAction.ExternalUrl ? ad.ButtonUrl ?? string.Empty : string.Empty,
                ButtonItemId = ad.ButtonAction == AdButtonAction.JellyfinItem ? ad.ButtonItemId ?? string.Empty : string.Empty
            };
        }

        /// <summary>
        /// Validates and normalises an incoming advertisement. Bad input is rejected explicitly
        /// (returns false with a message) rather than silently rewritten.
        /// </summary>
        private bool TryNormalize(Advertisement ad, out string error)
        {
            error = string.Empty;

            ad.Name = Trim(ad.Name, 200);
            ad.Title = Trim(ad.Title, 300);
            ad.Description = Trim(ad.Description, 4000);
            ad.ButtonText = Trim(ad.ButtonText, 100);
            ad.ObjectFit = ad.ObjectFit == "cover" ? "cover" : "contain";

            if (string.IsNullOrWhiteSpace(ad.Name))
            {
                error = "El nombre es obligatorio.";
                return false;
            }

            // File names: reject anything that is not a safe bare name.
            ad.MediaFile ??= string.Empty;
            ad.BackgroundFile ??= string.Empty;

            if (ad.MediaFile.Length > 0 && !_files.IsValidFileName(ad.MediaFile))
            {
                error = $"Nombre de archivo no válido: '{ad.MediaFile}'.";
                return false;
            }

            if (ad.BackgroundFile.Length > 0 && !_files.IsValidFileName(ad.BackgroundFile))
            {
                error = $"Nombre de archivo de fondo no válido: '{ad.BackgroundFile}'.";
                return false;
            }

            if (ad.Type is AdvertisementType.Image or AdvertisementType.Video && ad.MediaFile.Length == 0)
            {
                error = "Los anuncios de imagen o vídeo requieren un archivo.";
                return false;
            }

            if (ad.DurationSeconds < 1 || ad.DurationSeconds > 600)
            {
                error = "La duración debe estar entre 1 y 600 segundos.";
                return false;
            }

            if (ad.SkipAfterSeconds < 0 || ad.SkipAfterSeconds > 600)
            {
                error = "«Permitir omitir después de» debe estar entre 0 y 600 segundos.";
                return false;
            }

            ad.Priority = Math.Clamp(ad.Priority, 0, 1000);
            ad.Order = Math.Clamp(ad.Order, 0, 1_000_000);

            if (!ValidTime(ad.StartTime) || !ValidTime(ad.EndTime))
            {
                error = "Las horas deben tener el formato HH:mm.";
                return false;
            }

            ad.StartTime = EmptyToNull(ad.StartTime);
            ad.EndTime = EmptyToNull(ad.EndTime);

            if (ad.StartDate is { } sd && ad.EndDate is { } ed && ed.Date < sd.Date)
            {
                error = "La fecha de finalización es anterior a la de inicio.";
                return false;
            }

            // Button action.
            switch (ad.ButtonAction)
            {
                case AdButtonAction.ExternalUrl:
                    if (!IsSafeExternalUrl(ad.ButtonUrl))
                    {
                        error = "La URL del botón debe empezar por http:// o https://.";
                        return false;
                    }

                    ad.ButtonItemId = string.Empty;
                    break;

                case AdButtonAction.JellyfinItem:
                    if (!Guid.TryParse(ad.ButtonItemId, out var itemGuid) || itemGuid == Guid.Empty)
                    {
                        error = "El identificador de contenido de Jellyfin no es válido.";
                        return false;
                    }

                    if (_libraryManager.GetItemById(itemGuid) is null)
                    {
                        error = "No existe ningún contenido de Jellyfin con ese identificador.";
                        return false;
                    }

                    ad.ButtonUrl = string.Empty;
                    break;

                default:
                    ad.ButtonUrl = string.Empty;
                    ad.ButtonItemId = string.Empty;
                    break;
            }

            ad.AllowedUserIds = (ad.AllowedUserIds ?? new List<string>())
                .Where(x => Guid.TryParse(x, out _))
                .Select(x => Guid.Parse(x).ToString())
                .Distinct()
                .ToList();

            ad.DaysOfWeek = (ad.DaysOfWeek ?? new List<int>())
                .Where(d => d is >= 0 and <= 6)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            return true;
        }

        private static bool IsSafeExternalUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
                   && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        private static bool ValidTime(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            return TimeSpan.TryParseExact(
                value.Trim(),
                new[] { @"hh\:mm", @"h\:mm" },
                CultureInfo.InvariantCulture,
                out _);
        }

        private static string? EmptyToNull(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string Trim(string? value, int max)
        {
            value ??= string.Empty;
            value = value.Trim();
            return value.Length > max ? value[..max] : value;
        }

        private static string? ReadEmbedded(string resource)
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream(resource);
            if (stream is null)
            {
                return null;
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
