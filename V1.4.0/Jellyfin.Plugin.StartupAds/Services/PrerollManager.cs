using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.StartupAds.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StartupAds.Services
{
    /// <summary>
    /// CRUD and selection for pre-roll ads (the "ads before every movie/episode" feature).
    /// </summary>
    public class PrerollManager
    {
        private readonly ILogger<PrerollManager> _logger;

        public PrerollManager(ILogger<PrerollManager> logger)
        {
            _logger = logger;
        }

        private static PrerollConfiguration Config =>
            Plugin.Instance?.Configuration.Preroll ?? new PrerollConfiguration();

        private static void Save() => Plugin.Instance?.SaveConfiguration();

        public IReadOnlyList<PrerollAd> GetAll() => Config.Advertisements.ToList();

        public PrerollAd? Get(Guid id) => Config.Advertisements.FirstOrDefault(a => a.Id == id);

        public PrerollAd Create(PrerollAd ad)
        {
            var cfg = Config;
            if (ad.Id == Guid.Empty)
            {
                ad.Id = Guid.NewGuid();
            }

            if (ad.Order == 0)
            {
                ad.Order = cfg.Advertisements.Count == 0 ? 1 : cfg.Advertisements.Max(a => a.Order) + 1;
            }

            cfg.Advertisements.Add(ad);
            Save();
            _logger.LogInformation("[StartupAds] Pre-roll created: {Name}", ad.Name);
            return ad;
        }

        public PrerollAd? Update(PrerollAd ad)
        {
            var cfg = Config;
            var idx = cfg.Advertisements.FindIndex(a => a.Id == ad.Id);
            if (idx < 0)
            {
                return null;
            }

            cfg.Advertisements[idx] = ad;
            Save();
            return ad;
        }

        public bool Delete(Guid id)
        {
            var removed = Config.Advertisements.RemoveAll(a => a.Id == id) > 0;
            if (removed)
            {
                Save();
            }

            return removed;
        }

        public bool SetEnabled(Guid id, bool enabled)
        {
            var ad = Get(id);
            if (ad is null)
            {
                return false;
            }

            ad.Enabled = enabled;
            Save();
            return true;
        }

        public PrerollAd? Duplicate(Guid id)
        {
            var src = Get(id);
            if (src is null)
            {
                return null;
            }

            return Create(new PrerollAd
            {
                Id = Guid.NewGuid(),
                Name = src.Name + " (copia)",
                ItemId = src.ItemId,
                ItemName = src.ItemName,
                Enabled = false,
                Priority = src.Priority,
                Order = 0,
                StartDate = src.StartDate,
                EndDate = src.EndDate,
                DaysOfWeek = new List<int>(src.DaysOfWeek),
                StartTime = src.StartTime,
                EndTime = src.EndTime,
                AllowedUserIds = new List<string>(src.AllowedUserIds)
            });
        }

        /// <summary>
        /// Pure selection: enabled → schedule → user targeting → ordering → optional random pick
        /// → <see cref="PrerollConfiguration.MaxPerPlayback"/> cap.
        /// </summary>
        public static IReadOnlyList<PrerollAd> Select(PrerollConfiguration cfg, string userId, DateTime nowLocal)
        {
            var candidates = cfg.Advertisements
                .Where(a => a.Enabled && !string.IsNullOrWhiteSpace(a.ItemId))
                .Where(a => IsWithinSchedule(a, nowLocal))
                .Where(a => IsUserTargeted(a, userId))
                .ToList();

            if (candidates.Count == 0)
            {
                return Array.Empty<PrerollAd>();
            }

            candidates = cfg.OrderMode switch
            {
                AdOrderMode.Priority => candidates.OrderByDescending(a => a.Priority).ThenBy(a => a.Order).ToList(),
                AdOrderMode.Name => candidates.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList(),
                AdOrderMode.Manual => candidates.OrderBy(a => a.Order).ToList(),
                AdOrderMode.Random => Shuffle(candidates),
                _ => candidates
            };

            if (cfg.RandomPick && candidates.Count > 1)
            {
                candidates = new List<PrerollAd> { candidates[Random.Shared.Next(candidates.Count)] };
            }

            return candidates.Take(Math.Max(1, cfg.MaxPerPlayback)).ToList();
        }

        public static bool IsWithinSchedule(PrerollAd a, DateTime nowLocal)
        {
            if (a.StartDate is { } sd && nowLocal.Date < sd.Date)
            {
                return false;
            }

            if (a.EndDate is { } ed && nowLocal.Date > ed.Date)
            {
                return false;
            }

            if (a.DaysOfWeek.Count > 0 && !a.DaysOfWeek.Contains((int)nowLocal.DayOfWeek))
            {
                return false;
            }

            return AdvertisementManager.IsWithinTimeWindow(a.StartTime, a.EndTime, nowLocal.TimeOfDay);
        }

        public static bool IsUserTargeted(PrerollAd a, string userId)
        {
            if (a.AllowedUserIds.Count == 0)
            {
                return true;
            }

            return Guid.TryParse(userId, out var uid)
                   && a.AllowedUserIds.Any(id => Guid.TryParse(id, out var g) && g == uid);
        }

        private static List<PrerollAd> Shuffle(List<PrerollAd> input)
        {
            var arr = input.ToArray();
            for (var i = arr.Length - 1; i > 0; i--)
            {
                var j = Random.Shared.Next(i + 1);
                (arr[i], arr[j]) = (arr[j], arr[i]);
            }

            return arr.ToList();
        }
    }
}
