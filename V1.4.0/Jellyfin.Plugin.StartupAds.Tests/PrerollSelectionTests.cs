using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.StartupAds.Configuration;
using Jellyfin.Plugin.StartupAds.Services;
using Xunit;

namespace Jellyfin.Plugin.StartupAds.Tests
{
    public class PrerollSelectionTests
    {
        private static readonly DateTime Now = new(2026, 9, 15, 12, 0, 0); // martes
        private const string User = "11111111-1111-1111-1111-111111111111";
        private const string OtherUser = "22222222-2222-2222-2222-222222222222";
        private const string Video = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

        private static PrerollConfiguration Cfg(Action<PrerollConfiguration>? tweak = null)
        {
            var c = new PrerollConfiguration
            {
                Enabled = true,
                OrderMode = AdOrderMode.Priority,
                MaxPerPlayback = 10,
                RandomPick = false
            };
            tweak?.Invoke(c);
            return c;
        }

        private static PrerollAd Ad(string name, int priority = 5, bool enabled = true, string? itemId = Video) => new()
        {
            Name = name,
            ItemId = itemId ?? string.Empty,
            ItemName = name,
            Priority = priority,
            Enabled = enabled
        };

        private static IReadOnlyList<PrerollAd> Run(PrerollConfiguration cfg, string user = User)
            => PrerollManager.Select(cfg, user, Now);

        [Fact]
        public void DisabledOrItemlessAdsAreExcluded()
        {
            var cfg = Cfg(c => c.Advertisements.AddRange(new[]
            {
                Ad("ok"),
                Ad("disabled", enabled: false),
                Ad("noItem", itemId: null)
            }));
            var result = Run(cfg);
            Assert.Equal(new[] { "ok" }, result.Select(a => a.Name).ToArray());
        }

        [Fact]
        public void PriorityHigherNumberWinsFirst()
        {
            var cfg = Cfg(c => c.Advertisements.AddRange(new[] { Ad("low", 10), Ad("high", 100), Ad("mid", 50) }));
            Assert.Equal(new[] { "high", "mid", "low" }, Run(cfg).Select(a => a.Name).ToArray());
        }

        [Fact]
        public void UserTargetingRespected()
        {
            var targeted = Ad("targeted");
            targeted.AllowedUserIds.Add(User);
            var cfg = Cfg(c => c.Advertisements.AddRange(new[] { targeted, Ad("everyone") }));

            Assert.Equal(2, Run(cfg, User).Count);
            Assert.Equal(new[] { "everyone" }, Run(cfg, OtherUser).Select(a => a.Name).ToArray());
        }

        [Fact]
        public void OutOfDateRangeExcluded()
        {
            var expired = Ad("expired");
            expired.EndDate = new DateTime(2026, 1, 1);
            var cfg = Cfg(c => c.Advertisements.AddRange(new[] { expired, Ad("current") }));
            Assert.Equal(new[] { "current" }, Run(cfg).Select(a => a.Name).ToArray());
        }

        [Fact]
        public void DayOfWeekFilterExcludesOtherDays()
        {
            var mondayOnly = Ad("monday");
            mondayOnly.DaysOfWeek.Add((int)DayOfWeek.Monday);
            var cfg = Cfg(c => c.Advertisements.Add(mondayOnly));
            Assert.Empty(Run(cfg)); // Now is a Tuesday
        }

        [Fact]
        public void TimeWindowRespected()
        {
            var evening = Ad("evening");
            evening.StartTime = "20:00";
            evening.EndTime = "23:00";
            var cfg = Cfg(c => c.Advertisements.Add(evening));
            Assert.Empty(Run(cfg)); // Now is 12:00
        }

        [Fact]
        public void MaxPerPlaybackCapsResult()
        {
            var cfg = Cfg(c =>
            {
                c.MaxPerPlayback = 2;
                c.Advertisements.AddRange(new[] { Ad("a", 3), Ad("b", 2), Ad("c", 1) });
            });
            Assert.Equal(new[] { "a", "b" }, Run(cfg).Select(a => a.Name).ToArray());
        }

        [Fact]
        public void RandomPickReturnsExactlyOneEligibleAd()
        {
            var cfg = Cfg(c =>
            {
                c.RandomPick = true;
                c.Advertisements.AddRange(new[] { Ad("a"), Ad("b"), Ad("c") });
            });
            for (var i = 0; i < 20; i++)
            {
                var result = Run(cfg);
                Assert.Single(result);
                Assert.Contains(result[0].Name, new[] { "a", "b", "c" });
            }
        }

        [Fact]
        public void OrderModeNameSortsAlphabetically()
        {
            var cfg = Cfg(c =>
            {
                c.OrderMode = AdOrderMode.Name;
                c.Advertisements.AddRange(new[] { Ad("Charlie", 1), Ad("alpha", 100), Ad("Bravo", 50) });
            });
            Assert.Equal(new[] { "alpha", "Bravo", "Charlie" }, Run(cfg).Select(a => a.Name).ToArray());
        }
    }
}
