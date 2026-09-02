using System;
using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.StartupAds.Configuration
{
    /// <summary>
    /// Order strategy for the advertisement queue shown on startup.
    /// </summary>
    public enum AdOrderMode
    {
        Priority = 0,
        Name = 1,
        Random = 2,
        Manual = 3
    }

    /// <summary>
    /// How advertisements are sourced.
    /// </summary>
    public enum AdSourceMode
    {
        Manual = 0,
        Automatic = 1,
        Mixed = 2
    }

    /// <summary>
    /// How often the overlay is shown to a given browser.
    /// </summary>
    public enum AdFrequencyMode
    {
        EveryStartup = 0,
        OncePerSession = 1
    }

    /// <summary>
    /// Visual presentation of the overlay.
    /// </summary>
    public enum AdDisplayMode
    {
        Modal = 0,
        Fullscreen = 1,
        CenterBanner = 2
    }

    /// <summary>
    /// When the skip control becomes usable.
    /// </summary>
    public enum SkipButtonMode
    {
        /// <summary>Button is visible but disabled until the countdown ends.</summary>
        DisabledUntilCountdown = 0,

        /// <summary>Button only appears once the countdown ends.</summary>
        AppearsAfterCountdown = 1
    }

    /// <summary>
    /// Persisted plugin configuration. Serialized to
    /// <c>plugins/configurations/Jellyfin.Plugin.StartupAds.xml</c> by Jellyfin.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        public PluginConfiguration()
        {
            Enabled = true;
            ShowOnStartup = true;
            AdsDirectory = string.Empty;
            SourceMode = AdSourceMode.Mixed;
            OrderMode = AdOrderMode.Priority;
            FrequencyMode = AdFrequencyMode.OncePerSession;
            DisplayMode = AdDisplayMode.Modal;

            DefaultDurationSeconds = 10;
            MaxAdsPerStartup = 1;
            RandomPick = false;

            ShowCountdown = true;
            AllowSkip = true;
            SkipAfterSeconds = 5;
            SkipButtonMode = SkipButtonMode.DisabledUntilCountdown;
            ShowCloseButton = false;
            AllowCloseWithEscape = true;

            AutoplayVideo = true;
            MutedVideo = true;
            LoopVideo = false;
            ShowVideoControls = false;

            OverlayOpacity = 0.85;
            MaxWidthPx = 900;
            MaxHeightPx = 700;
            BorderRadiusPx = 14;
            ObjectFit = "contain";
            AccentColor = "#00a4dc";

            EnableStatistics = false;
            Language = "es";

            Advertisements = new List<Advertisement>();
            Statistics = new List<AdStat>();
        }

        // ---- General ----
        public bool Enabled { get; set; }

        public bool ShowOnStartup { get; set; }

        /// <summary>Absolute path to the folder that contains ad media files.</summary>
        public string AdsDirectory { get; set; }

        public AdSourceMode SourceMode { get; set; }

        public AdOrderMode OrderMode { get; set; }

        public AdFrequencyMode FrequencyMode { get; set; }

        public AdDisplayMode DisplayMode { get; set; }

        public int DefaultDurationSeconds { get; set; }

        public int MaxAdsPerStartup { get; set; }

        public bool RandomPick { get; set; }

        // ---- Countdown / skip ----
        public bool ShowCountdown { get; set; }

        public bool AllowSkip { get; set; }

        public int SkipAfterSeconds { get; set; }

        public SkipButtonMode SkipButtonMode { get; set; }

        public bool ShowCloseButton { get; set; }

        public bool AllowCloseWithEscape { get; set; }

        // ---- Video ----
        public bool AutoplayVideo { get; set; }

        public bool MutedVideo { get; set; }

        public bool LoopVideo { get; set; }

        public bool ShowVideoControls { get; set; }

        // ---- Appearance ----
        public double OverlayOpacity { get; set; }

        public int MaxWidthPx { get; set; }

        public int MaxHeightPx { get; set; }

        public int BorderRadiusPx { get; set; }

        /// <summary>"contain" or "cover".</summary>
        public string ObjectFit { get; set; }

        public string AccentColor { get; set; }

        // ---- Misc ----
        public bool EnableStatistics { get; set; }

        public string Language { get; set; }

        /// <summary>All manually managed advertisements.</summary>
        public List<Advertisement> Advertisements { get; set; }

        /// <summary>Aggregated per-advertisement counters (opt-in).</summary>
        public List<AdStat> Statistics { get; set; }
    }

    public class AdStat
    {
        public Guid AdvertisementId { get; set; }

        public long Shown { get; set; }

        public long Skipped { get; set; }

        public long Completed { get; set; }

        public long Clicked { get; set; }
    }
}
