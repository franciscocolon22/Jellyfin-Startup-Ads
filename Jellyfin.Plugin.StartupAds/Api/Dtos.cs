using System;
using System.Collections.Generic;
using Jellyfin.Plugin.StartupAds.Configuration;

namespace Jellyfin.Plugin.StartupAds.Api
{
    /// <summary>Global, non-sensitive settings sent to the browser.</summary>
    public class ClientBootstrapDto
    {
        public bool Enabled { get; set; }

        public string DisplayMode { get; set; } = "Modal";

        public string FrequencyMode { get; set; } = "OncePerSession";

        public bool ShowCountdown { get; set; }

        public int DefaultDurationSeconds { get; set; }

        /// <summary>"DisabledUntilCountdown" or "AppearsAfterCountdown".</summary>
        public string SkipButtonMode { get; set; } = "DisabledUntilCountdown";

        public bool ShowCloseButton { get; set; }

        public bool AllowCloseWithEscape { get; set; }

        public bool AutoplayVideo { get; set; }

        public bool MutedVideo { get; set; }

        public bool LoopVideo { get; set; }

        public bool ShowVideoControls { get; set; }

        public double OverlayOpacity { get; set; }

        public int MaxWidthPx { get; set; }

        public int MaxHeightPx { get; set; }

        public int BorderRadiusPx { get; set; }

        public string AccentColor { get; set; } = "#00a4dc";

        public string Language { get; set; } = "es";

        public bool StatisticsEnabled { get; set; }

        public List<ClientAdDto> Ads { get; set; } = new();
    }

    /// <summary>A single advertisement as consumed by the frontend.</summary>
    public class ClientAdDto
    {
        public string Id { get; set; } = string.Empty;

        public string Type { get; set; } = "Image";

        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        /// <summary>Relative media URL (no auth token). The client appends api_key.</summary>
        public string? MediaUrl { get; set; }

        public string? BackgroundUrl { get; set; }

        public string ObjectFit { get; set; } = "contain";

        public int DurationSeconds { get; set; }

        public bool UseVideoDuration { get; set; }

        public bool AllowSkip { get; set; }

        public int SkipAfterSeconds { get; set; }

        public bool ShowCountdown { get; set; }

        public string ButtonText { get; set; } = string.Empty;

        public string ButtonAction { get; set; } = "None";

        public string ButtonUrl { get; set; } = string.Empty;

        public string ButtonItemId { get; set; } = string.Empty;
    }

    public class ValidatePathRequest
    {
        public string? Path { get; set; }
    }
}
