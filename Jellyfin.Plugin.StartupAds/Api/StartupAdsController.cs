using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using Jellyfin.Plugin.StartupAds.Configuration;
using Jellyfin.Plugin.StartupAds.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StartupAds.Api
{
    /// <summary>
    /// All HTTP endpoints for the Startup Ads plugin. User endpoints require a valid Jellyfin
    /// session; admin endpoints additionally require elevation.
    /// </summary>
    [ApiController]
    [Route("StartupAds")]
    public class StartupAdsController : ControllerBase
    {
        // Jellyfin injects the internal user id under this claim.
        private const string UserIdClaim = "Jellyfin-UserId";

        private readonly ILogger<StartupAdsController> _logger;
        private readonly AdvertisementManager _manager;
        private readonly MediaFileService _files;

        public StartupAdsController(
            ILogger<StartupAdsController> logger,
            AdvertisementManager manager,
            MediaFileService files)
        {
            _logger = logger;
            _manager = manager;
            _files = files;
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
        // Public assets (loaded by the injected <script> tag, no auth possible)
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

            return Content(css, "text/css");
        }

        // ---------------------------------------------------------------------
        // User-facing API
        // ---------------------------------------------------------------------
        [HttpGet("Config")]
        [Authorize(Policy = "DefaultAuthorization")]
        public ActionResult<ClientBootstrapDto> GetConfig()
        {
            var cfg = Config;
            var userId = CurrentUserId();
            var now = DateTime.Now;

            var dto = new ClientBootstrapDto
            {
                Enabled = cfg.Enabled && cfg.ShowOnStartup,
                DisplayMode = cfg.DisplayMode.ToString(),
                FrequencyMode = cfg.FrequencyMode.ToString(),
                ShowCountdown = cfg.ShowCountdown,
                DefaultDurationSeconds = cfg.DefaultDurationSeconds,
                SkipButtonMode = cfg.SkipButtonMode.ToString(),
                ShowCloseButton = cfg.ShowCloseButton,
                AllowCloseWithEscape = cfg.AllowCloseWithEscape,
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
                StatisticsEnabled = cfg.EnableStatistics
            };

            if (!dto.Enabled)
            {
                return dto;
            }

            foreach (var ad in _manager.GetActiveForUser(userId, now))
            {
                dto.Ads.Add(ToClientDto(ad, cfg));
            }

            return dto;
        }

        [HttpGet("Media/{adId}")]
        [Authorize(Policy = "DefaultAuthorization")]
        public ActionResult GetMedia([FromRoute] Guid adId)
        {
            var cfg = Config;
            var ad = _manager.Get(adId);
            if (ad is null || !ad.Enabled)
            {
                return NotFound();
            }

            // Extra guard: the requesting user must actually be targeted by this ad.
            if (ad.AllowedUserIds.Count > 0)
            {
                var uid = CurrentUserId();
                if (!ad.AllowedUserIds.Any(x => Guid.TryParse(x, out var g) && g == uid))
                {
                    return Forbid();
                }
            }

            var path = _files.ResolveFile(cfg.AdsDirectory, ad.MediaFile);
            if (path is null)
            {
                return NotFound();
            }

            return PhysicalFile(path, MediaFileService.ContentTypeFor(path), enableRangeProcessing: true);
        }

        [HttpGet("Media/{adId}/Background")]
        [Authorize(Policy = "DefaultAuthorization")]
        public ActionResult GetBackground([FromRoute] Guid adId)
        {
            var cfg = Config;
            var ad = _manager.Get(adId);
            if (ad is null || string.IsNullOrEmpty(ad.BackgroundFile))
            {
                return NotFound();
            }

            var path = _files.ResolveFile(cfg.AdsDirectory, ad.BackgroundFile);
            if (path is null)
            {
                return NotFound();
            }

            return PhysicalFile(path, MediaFileService.ContentTypeFor(path), enableRangeProcessing: true);
        }

        [HttpPost("Track/{adId}/{kind}")]
        [Authorize(Policy = "DefaultAuthorization")]
        public ActionResult Track([FromRoute] Guid adId, [FromRoute] string kind)
        {
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

            // Preserve the ad list / stats which are managed through their own endpoints.
            incoming.Advertisements = p.Configuration.Advertisements;
            incoming.Statistics = p.Configuration.Statistics;

            incoming.DefaultDurationSeconds = Math.Clamp(incoming.DefaultDurationSeconds, 1, 600);
            incoming.SkipAfterSeconds = Math.Clamp(incoming.SkipAfterSeconds, 0, 600);
            incoming.MaxAdsPerStartup = Math.Clamp(incoming.MaxAdsPerStartup, 1, 20);
            incoming.OverlayOpacity = Math.Clamp(incoming.OverlayOpacity, 0d, 1d);

            p.UpdateConfiguration(incoming);
            _logger.LogInformation("[StartupAds] Configuration saved.");
            return NoContent();
        }

        [HttpGet("Admin/Advertisements")]
        [Authorize(Policy = "RequiresElevation")]
        public ActionResult<IReadOnlyList<Advertisement>> GetAds() => Ok(_manager.GetAll());

        [HttpPost("Admin/Advertisements")]
        [Authorize(Policy = "RequiresElevation")]
        public ActionResult<Advertisement> CreateAd([FromBody] Advertisement ad)
        {
            Sanitize(ad);
            return Ok(_manager.Create(ad));
        }

        [HttpPost("Admin/Advertisements/{id}")]
        [Authorize(Policy = "RequiresElevation")]
        public ActionResult<Advertisement> UpdateAd([FromRoute] Guid id, [FromBody] Advertisement ad)
        {
            ad.Id = id;
            Sanitize(ad);
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
        public ActionResult<object> Scan()
        {
            var count = _manager.ScanAndImport();
            return Ok(new { imported = count });
        }

        [HttpGet("Admin/Preview")]
        [Authorize(Policy = "RequiresElevation")]
        public ActionResult<ClientBootstrapDto> Preview([FromQuery] Guid? adId)
        {
            var cfg = Config;
            var dto = new ClientBootstrapDto
            {
                Enabled = true,
                DisplayMode = cfg.DisplayMode.ToString(),
                FrequencyMode = "EveryStartup",
                ShowCountdown = cfg.ShowCountdown,
                DefaultDurationSeconds = cfg.DefaultDurationSeconds,
                SkipButtonMode = cfg.SkipButtonMode.ToString(),
                ShowCloseButton = true,
                AllowCloseWithEscape = true,
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
                StatisticsEnabled = false
            };

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
        private static ClientAdDto ToClientDto(Advertisement ad, PluginConfiguration cfg)
        {
            var hasMedia = !string.IsNullOrEmpty(ad.MediaFile);
            return new ClientAdDto
            {
                Id = ad.Id.ToString(),
                Type = ad.Type.ToString(),
                Title = ad.Title ?? string.Empty,
                Description = ad.Description ?? string.Empty,
                MediaUrl = hasMedia ? $"StartupAds/Media/{ad.Id}" : null,
                BackgroundUrl = string.IsNullOrEmpty(ad.BackgroundFile)
                    ? null
                    : $"StartupAds/Media/{ad.Id}/Background",
                ObjectFit = string.IsNullOrWhiteSpace(ad.ObjectFit) ? cfg.ObjectFit : ad.ObjectFit,
                DurationSeconds = ad.DurationSeconds > 0 ? ad.DurationSeconds : cfg.DefaultDurationSeconds,
                UseVideoDuration = ad.Type == AdvertisementType.Video && ad.DurationMode == AdDurationMode.FromVideo,
                AllowSkip = ad.AllowSkip && cfg.AllowSkip,
                SkipAfterSeconds = Math.Max(0, ad.SkipAfterSeconds),
                ShowCountdown = ad.ShowCountdown && cfg.ShowCountdown,
                ButtonText = ad.ButtonText ?? string.Empty,
                ButtonAction = ad.ButtonAction.ToString(),
                ButtonUrl = ad.ButtonUrl ?? string.Empty,
                ButtonItemId = ad.ButtonItemId ?? string.Empty
            };
        }

        private void Sanitize(Advertisement ad)
        {
            ad.Name = Trim(ad.Name, 200);
            ad.Title = Trim(ad.Title, 300);
            ad.Description = Trim(ad.Description, 4000);
            ad.ButtonText = Trim(ad.ButtonText, 100);
            ad.MediaFile = Path.GetFileName(ad.MediaFile ?? string.Empty);
            ad.BackgroundFile = Path.GetFileName(ad.BackgroundFile ?? string.Empty);
            ad.DurationSeconds = Math.Clamp(ad.DurationSeconds, 1, 600);
            ad.SkipAfterSeconds = Math.Clamp(ad.SkipAfterSeconds, 0, 600);
            ad.Priority = Math.Clamp(ad.Priority, 0, 1000);

            if (ad.ButtonAction == AdButtonAction.ExternalUrl
                && !string.IsNullOrWhiteSpace(ad.ButtonUrl)
                && !ad.ButtonUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                && !ad.ButtonUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("[StartupAds] Button URL rejected (not http/https): {Url}", ad.ButtonUrl);
                ad.ButtonUrl = string.Empty;
                ad.ButtonAction = AdButtonAction.None;
            }

            ad.AllowedUserIds = (ad.AllowedUserIds ?? new List<string>())
                .Where(x => Guid.TryParse(x, out _))
                .Distinct()
                .ToList();

            ad.DaysOfWeek = (ad.DaysOfWeek ?? new List<int>())
                .Where(d => d is >= 0 and <= 6)
                .Distinct()
                .ToList();
        }

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
