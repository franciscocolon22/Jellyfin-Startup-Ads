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

    /// <summary>What content types a pre-roll ad plays before.</summary>
    public enum PrerollAppliesTo
    {
        Movies = 0,
        Episodes = 1,
        MoviesAndEpisodes = 2
    }

    /// <summary>How often a pre-roll is shown to a user.</summary>
    public enum PrerollFrequency
    {
        EveryPlayback = 0,
        OncePerDay = 1,
        RandomChance = 2
    }

    /// <summary>
    /// A single pre-roll advertisement: an existing Jellyfin library video played before content.
    /// </summary>
    public class PrerollAd
    {
        public PrerollAd()
        {
            Id = Guid.NewGuid();
            Name = string.Empty;
            ItemId = string.Empty;
            ItemName = string.Empty;
            Enabled = true;
            Priority = 5;
            Order = 0;
            DaysOfWeek = new List<int>();
            AllowedUserIds = new List<string>();
        }

        public Guid Id { get; set; }

        public string Name { get; set; }

        /// <summary>GUID (string form) of the Jellyfin library video to play.</summary>
        public string ItemId { get; set; }

        /// <summary>Display name of that item (informational).</summary>
        public string ItemName { get; set; }

        public bool Enabled { get; set; }

        /// <summary>Higher = shown first. 0–1000.</summary>
        public int Priority { get; set; }

        public int Order { get; set; }

        public DateTime? StartDate { get; set; }

        public DateTime? EndDate { get; set; }

        /// <summary>0=Sunday .. 6=Saturday. Empty = every day.</summary>
        public List<int> DaysOfWeek { get; set; }

        /// <summary>"HH:mm" local time. Null/empty = no restriction. End &lt; Start crosses midnight.</summary>
        public string? StartTime { get; set; }

        public string? EndTime { get; set; }

        /// <summary>Empty = all users. Otherwise only these Jellyfin user ids (string form).</summary>
        public List<string> AllowedUserIds { get; set; }
    }

    public class PrerollShown
    {
        public string UserId { get; set; } = string.Empty;

        public DateTime Date { get; set; }
    }

    /// <summary>Configuration for the "ads before every movie/episode" (pre-roll) feature.</summary>
    public class PrerollConfiguration
    {
        public PrerollConfiguration()
        {
            Enabled = false;
            AppliesTo = PrerollAppliesTo.MoviesAndEpisodes;
            MaxPerPlayback = 1;
            OrderMode = AdOrderMode.Priority;
            RandomPick = false;
            Frequency = PrerollFrequency.EveryPlayback;
            RandomChancePercent = 100;
            Advertisements = new List<PrerollAd>();
            ShownLog = new List<PrerollShown>();
        }

        public bool Enabled { get; set; }

        public PrerollAppliesTo AppliesTo { get; set; }

        public int MaxPerPlayback { get; set; }

        public AdOrderMode OrderMode { get; set; }

        public bool RandomPick { get; set; }

        public PrerollFrequency Frequency { get; set; }

        /// <summary>Only for <see cref="PrerollFrequency.RandomChance"/> (0–100).</summary>
        public int RandomChancePercent { get; set; }

        public List<PrerollAd> Advertisements { get; set; }

        /// <summary>Per-user "shown a pre-roll on this date" log, for <see cref="PrerollFrequency.OncePerDay"/>.</summary>
        public List<PrerollShown> ShownLog { get; set; }
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

            InjectClientScript = true;
            WebBasePath = "/web";

            Advertisements = new List<Advertisement>();
            Statistics = new List<AdStat>();
            Preroll = new PrerollConfiguration();
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

        /// <summary>
        /// When true, the plugin injects its client &lt;script&gt; into jellyfin-web's index.html
        /// response (in memory). Turn off to disable the overlay entirely without uninstalling.
        /// </summary>
        public bool InjectClientScript { get; set; }

        /// <summary>
        /// Base path under which jellyfin-web is served. Almost always "/web"; change only if the
        /// server is configured with a custom web base path.
        /// </summary>
        public string WebBasePath { get; set; }

        /// <summary>All manually managed advertisements (startup overlay = "Presentación").</summary>
        public List<Advertisement> Advertisements { get; set; }

        /// <summary>Aggregated per-advertisement counters (opt-in).</summary>
        public List<AdStat> Statistics { get; set; }

        /// <summary>
        /// The independent "ads before every movie/episode" feature (works on native apps via
        /// Jellyfin's <c>IIntroProvider</c>). Its own list, own settings.
        /// </summary>
        public PrerollConfiguration Preroll { get; set; }
    }

    public class AdStat
    {
        public Guid AdvertisementId { get; set; }

        public long Impressions { get; set; }

        public long Started { get; set; }

        public long Skipped { get; set; }

        public long Completed { get; set; }

        public long Clicked { get; set; }
    }
}
