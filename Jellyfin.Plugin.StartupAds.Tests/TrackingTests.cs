using Jellyfin.Plugin.StartupAds.Services;
using Xunit;

namespace Jellyfin.Plugin.StartupAds.Tests
{
    public class TrackingTests
    {
        [Theory]
        [InlineData("impression")]
        [InlineData("started")]
        [InlineData("completed")]
        [InlineData("skipped")]
        [InlineData("clicked")]
        public void ValidEventsAccepted(string kind)
        {
            Assert.True(AdvertisementManager.IsValidTrackingEvent(kind));
        }

        [Theory]
        [InlineData("shown")]
        [InlineData("COMPLETED")]
        [InlineData("' OR 1=1")]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("completed ")]
        public void InvalidEventsRejected(string? kind)
        {
            Assert.False(AdvertisementManager.IsValidTrackingEvent(kind));
        }

        [Fact]
        public void EventSetIsExactlyTheFiveDocumentedValues()
        {
            Assert.Equal(5, AdvertisementManager.TrackingEvents.Count);
        }
    }
}
