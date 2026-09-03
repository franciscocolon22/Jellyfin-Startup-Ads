using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.StartupAds.Configuration;
using Jellyfin.Plugin.StartupAds.Services;
using Xunit;

namespace Jellyfin.Plugin.StartupAds.Tests
{
    /// <summary>Rules for "Escanear e importar vídeos" of the pre-roll folder.</summary>
    public class PrerollScanTests
    {
        private static readonly Guid A = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        private static readonly Guid B = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
        private static readonly Guid C = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");

        private static (Guid, string) V(Guid id, string name) => (id, name);

        [Fact]
        public void ImportsOneAdPerNewVideo()
        {
            var ads = new List<PrerollAd>();
            var r = PrerollManager.Reconcile(ads, new[] { V(A, "Bumper 1"), V(B, "Bumper 2") });

            Assert.Equal(2, r.Imported);
            Assert.Equal(0, r.RemovedMissing);
            Assert.Equal(2, ads.Count);
            Assert.All(ads, a => Assert.True(a.AutoImported));
            Assert.All(ads, a => Assert.True(a.Enabled));
            Assert.Contains(ads, a => a.Name == "Bumper 1" && a.ItemId == A.ToString());
        }

        [Fact]
        public void DoesNotDuplicateAlreadyReferencedVideos()
        {
            var ads = new List<PrerollAd> { new() { Name = "hand", ItemId = A.ToString(), AutoImported = false } };
            var r = PrerollManager.Reconcile(ads, new[] { V(A, "same video"), V(B, "new one") });

            Assert.Equal(1, r.Imported);
            Assert.Equal(2, ads.Count);
            Assert.Single(ads, a => a.ItemId == A.ToString());
        }

        [Fact]
        public void RemovesAutoImportedAdsWhoseVideoIsGone()
        {
            var ads = new List<PrerollAd>
            {
                new() { Name = "gone", ItemId = C.ToString(), AutoImported = true },
                new() { Name = "still here", ItemId = A.ToString(), AutoImported = true }
            };
            var r = PrerollManager.Reconcile(ads, new[] { V(A, "still here") });

            Assert.Equal(0, r.Imported);
            Assert.Equal(1, r.RemovedMissing);
            Assert.Single(ads);
            Assert.Equal(A.ToString(), ads[0].ItemId);
        }

        [Fact]
        public void NeverRemovesHandMadeAdsEvenIfVideoIsGone()
        {
            var ads = new List<PrerollAd>
            {
                new() { Name = "manual", ItemId = C.ToString(), AutoImported = false }
            };
            var r = PrerollManager.Reconcile(ads, new[] { V(A, "other") });

            Assert.Equal(1, r.Imported);   // A added
            Assert.Equal(0, r.RemovedMissing);
            Assert.Contains(ads, a => a.ItemId == C.ToString());
        }

        [Fact]
        public void NewAdsGetIncreasingOrder()
        {
            var ads = new List<PrerollAd> { new() { Name = "x", ItemId = A.ToString(), Order = 7 } };
            PrerollManager.Reconcile(ads, new[] { V(B, "b"), V(C, "c") });

            var orders = ads.Where(a => a.ItemId != A.ToString()).Select(a => a.Order).OrderBy(o => o).ToArray();
            Assert.Equal(new[] { 8, 9 }, orders);
        }

        [Fact]
        public void IdempotentOnSecondRun()
        {
            var ads = new List<PrerollAd>();
            PrerollManager.Reconcile(ads, new[] { V(A, "a"), V(B, "b") });
            var r2 = PrerollManager.Reconcile(ads, new[] { V(A, "a"), V(B, "b") });

            Assert.Equal(0, r2.Imported);
            Assert.Equal(0, r2.RemovedMissing);
            Assert.Equal(2, ads.Count);
        }
    }
}
