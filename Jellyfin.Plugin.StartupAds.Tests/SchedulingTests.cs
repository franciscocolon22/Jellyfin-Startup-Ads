using System;
using Jellyfin.Plugin.StartupAds.Configuration;
using Jellyfin.Plugin.StartupAds.Services;
using Xunit;

namespace Jellyfin.Plugin.StartupAds.Tests
{
    public class SchedulingTests
    {
        private static Advertisement Ad() => new Advertisement();

        [Fact]
        public void BeforeStartDate_NotShown()
        {
            var ad = Ad();
            ad.StartDate = new DateTime(2026, 9, 10);
            Assert.False(AdvertisementManager.IsWithinSchedule(ad, new DateTime(2026, 9, 5, 12, 0, 0)));
        }

        [Fact]
        public void AfterEndDate_NotShown()
        {
            var ad = Ad();
            ad.EndDate = new DateTime(2026, 9, 10);
            Assert.False(AdvertisementManager.IsWithinSchedule(ad, new DateTime(2026, 9, 11, 12, 0, 0)));
        }

        [Fact]
        public void WithinDateRange_Shown()
        {
            var ad = Ad();
            ad.StartDate = new DateTime(2026, 9, 1);
            ad.EndDate = new DateTime(2026, 9, 30);
            Assert.True(AdvertisementManager.IsWithinSchedule(ad, new DateTime(2026, 9, 15, 12, 0, 0)));
        }

        [Fact]
        public void DayOfWeekFilter_Respected()
        {
            var ad = Ad();
            // 2026-09-15 is a Tuesday (DayOfWeek == 2).
            ad.DaysOfWeek.Add(1); // Monday only
            Assert.False(AdvertisementManager.IsWithinSchedule(ad, new DateTime(2026, 9, 15, 12, 0, 0)));
            ad.DaysOfWeek.Add(2);
            Assert.True(AdvertisementManager.IsWithinSchedule(ad, new DateTime(2026, 9, 15, 12, 0, 0)));
        }

        [Fact]
        public void TimeWindow_Respected()
        {
            var ad = Ad();
            ad.StartTime = "09:00";
            ad.EndTime = "18:00";
            Assert.False(AdvertisementManager.IsWithinSchedule(ad, new DateTime(2026, 9, 15, 8, 0, 0)));
            Assert.True(AdvertisementManager.IsWithinSchedule(ad, new DateTime(2026, 9, 15, 10, 0, 0)));
            Assert.False(AdvertisementManager.IsWithinSchedule(ad, new DateTime(2026, 9, 15, 19, 0, 0)));
        }

        [Fact]
        public void NoConstraints_AlwaysShown()
        {
            Assert.True(AdvertisementManager.IsWithinSchedule(Ad(), DateTime.Now));
        }
    }
}
