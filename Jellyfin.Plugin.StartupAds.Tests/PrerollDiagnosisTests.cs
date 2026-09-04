using System;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.StartupAds.Configuration;
using Jellyfin.Plugin.StartupAds.Services;
using Xunit;

namespace Jellyfin.Plugin.StartupAds.Tests
{
    /// <summary>Tests for <see cref="PrerollManager.Diagnose"/>, the self-check behind the Dashboard's diagnostic tool.</summary>
    public class PrerollDiagnosisTests
    {
        private static readonly DateTime Now = new(2026, 9, 15, 12, 0, 0); // martes
        private const string User = "11111111-1111-1111-1111-111111111111";

        private static PluginConfiguration Cfg(Action<PrerollConfiguration>? tweak = null)
        {
            var c = new PluginConfiguration { Enabled = true };
            c.Preroll.Enabled = true;
            tweak?.Invoke(c.Preroll);
            return c;
        }

        private static PrerollAd Ad(string name, bool enabled = true, string itemId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
            => new() { Name = name, Enabled = enabled, ItemId = itemId, Priority = 5 };

        private static readonly Func<PrerollAd, (bool, bool, bool)> AllGood = _ => (true, true, true);

        [Fact]
        public void PluginDisabledIsReportedFirst()
        {
            var cfg = Cfg();
            cfg.Enabled = false;
            cfg.Preroll.Advertisements.Add(Ad("a"));

            var r = PrerollManager.Diagnose(cfg, BaseItemKind.Movie, User, Now, false, AllGood);

            Assert.False(r.PluginEnabled);
            Assert.Equal(0, r.WouldPlayCount);
            Assert.Contains("plugin", r.Summary, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void PrerollDisabledIsReported()
        {
            var cfg = Cfg(p => p.Enabled = false);
            cfg.Preroll.Advertisements.Add(Ad("a"));

            var r = PrerollManager.Diagnose(cfg, BaseItemKind.Movie, User, Now, false, AllGood);

            Assert.False(r.PrerollEnabled);
            Assert.Equal(0, r.WouldPlayCount);
        }

        [Fact]
        public void NoAdsIsReported()
        {
            var cfg = Cfg();
            var r = PrerollManager.Diagnose(cfg, BaseItemKind.Movie, User, Now, false, AllGood);

            Assert.Equal(0, r.TotalAds);
            Assert.Contains("No hay ningún vídeo", r.Summary);
        }

        [Fact]
        public void AppliesToMismatchExcludesEveryAd()
        {
            var cfg = Cfg(p => p.AppliesTo = PrerollAppliesTo.Episodes);
            cfg.Preroll.Advertisements.Add(Ad("a"));

            var r = PrerollManager.Diagnose(cfg, BaseItemKind.Movie, User, Now, false, AllGood);

            Assert.False(r.AppliesToContentType);
            Assert.Equal(0, r.WouldPlayCount);
            Assert.Contains("Aplicar a", r.Ads[0].Reason);
        }

        [Fact]
        public void MissingLibraryAccessIsCalledOutPerAd()
        {
            var cfg = Cfg();
            cfg.Preroll.Advertisements.Add(Ad("sin acceso"));

            var r = PrerollManager.Diagnose(cfg, BaseItemKind.Movie, User, Now, false, _ => (true, false, true));

            Assert.False(r.Ads[0].UserCanSeeVideo);
            Assert.False(r.Ads[0].WouldPlay);
            Assert.Contains("acceso", r.Ads[0].Reason);
        }

        [Fact]
        public void NoPlayableMediaSourceIsCalledOut()
        {
            var cfg = Cfg();
            cfg.Preroll.Advertisements.Add(Ad("sin media"));

            var r = PrerollManager.Diagnose(cfg, BaseItemKind.Movie, User, Now, false, _ => (true, true, false));

            Assert.False(r.Ads[0].HasPlayableMedia);
            Assert.False(r.Ads[0].WouldPlay);
        }

        [Fact]
        public void OncePerDayAlreadyShownBlocksEverything()
        {
            var cfg = Cfg(p => p.Frequency = PrerollFrequency.OncePerDay);
            cfg.Preroll.Advertisements.Add(Ad("a"));

            var r = PrerollManager.Diagnose(cfg, BaseItemKind.Movie, User, Now, alreadyShownToday: true, AllGood);

            Assert.False(r.FrequencyAllowsNow);
            Assert.Equal(0, r.WouldPlayCount);
        }

        [Fact]
        public void EverythingOkReportsWouldPlay()
        {
            var cfg = Cfg();
            cfg.Preroll.Advertisements.Add(Ad("a"));

            var r = PrerollManager.Diagnose(cfg, BaseItemKind.Movie, User, Now, false, AllGood);

            Assert.Equal(1, r.WouldPlayCount);
            Assert.True(r.Ads[0].WouldPlay);
            Assert.Equal("Se reproduciría.", r.Ads[0].Reason);
            Assert.Contains("SÍ reproduciría", r.Summary);
        }

        [Fact]
        public void DisabledAdIsExcludedButOthersStillCounted()
        {
            var cfg = Cfg();
            cfg.Preroll.Advertisements.Add(Ad("off", enabled: false));
            cfg.Preroll.Advertisements.Add(Ad("on", itemId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));

            var r = PrerollManager.Diagnose(cfg, BaseItemKind.Movie, User, Now, false, AllGood);

            Assert.Equal(1, r.WouldPlayCount);
            Assert.False(r.Ads.Single(a => a.Name == "off").WouldPlay);
            Assert.True(r.Ads.Single(a => a.Name == "on").WouldPlay);
        }
    }
}
