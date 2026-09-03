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
            ad.DaysOfWeek.Add(1);
            Assert.False(AdvertisementManager.IsWithinSchedule(ad, new DateTime(2026, 9, 15, 12, 0, 0)));
            ad.DaysOfWeek.Add(2);
            Assert.True(AdvertisementManager.IsWithinSchedule(ad, new DateTime(2026, 9, 15, 12, 0, 0)));
        }

        [Theory]
        [InlineData("09:00", "18:00", "08:00", false)]
        [InlineData("09:00", "18:00", "09:00", true)]
        [InlineData("09:00", "18:00", "18:00", true)]
        [InlineData("09:00", "18:00", "19:00", false)]
        public void DaytimeWindow(string start, string end, string now, bool expected)
        {
            Assert.Equal(expected, AdvertisementManager.IsWithinTimeWindow(start, end, TimeSpan.Parse(now)));
        }

        // §11 - window that crosses midnight: 22:00 -> 02:00
        [Theory]
        [InlineData("21:59", false)]
        [InlineData("22:00", true)]
        [InlineData("23:00", true)]
        [InlineData("23:59", true)]
        [InlineData("00:00", true)]
        [InlineData("01:00", true)]
        [InlineData("01:59", true)]
        [InlineData("02:00", true)]
        [InlineData("02:01", false)]
        [InlineData("12:00", false)]
        public void MidnightCrossingWindow(string now, bool expected)
        {
            Assert.Equal(expected, AdvertisementManager.IsWithinTimeWindow("22:00", "02:00", TimeSpan.Parse(now)));
        }

        [Fact]
        public void OpenEndedWindows()
        {
            Assert.True(AdvertisementManager.IsWithinTimeWindow("08:00", null, TimeSpan.Parse("09:00")));
            Assert.False(AdvertisementManager.IsWithinTimeWindow("08:00", null, TimeSpan.Parse("07:00")));
            Assert.True(AdvertisementManager.IsWithinTimeWindow(null, "18:00", TimeSpan.Parse("17:00")));
            Assert.False(AdvertisementManager.IsWithinTimeWindow(null, "18:00", TimeSpan.Parse("19:00")));
        }

        [Fact]
        public void NoConstraints_AlwaysShown()
        {
            Assert.True(AdvertisementManager.IsWithinSchedule(Ad(), DateTime.Now));
            Assert.True(AdvertisementManager.IsWithinTimeWindow(null, null, TimeSpan.FromHours(3)));
        }
    }
}
