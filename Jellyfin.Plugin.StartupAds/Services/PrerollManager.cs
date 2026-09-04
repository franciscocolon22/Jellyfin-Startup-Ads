using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
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

        /// <summary>Result of <see cref="SyncFolder"/>.</summary>
        public sealed class ScanResult
        {
            public int Imported { get; set; }

            public int RemovedMissing { get; set; }

            public int Total { get; set; }
        }

        /// <summary>
        /// Reconciles the pre-roll ad list with the videos currently found in the pre-roll folder:
        /// adds one <see cref="PrerollAd"/> per new library video and removes previously
        /// auto-imported ads whose video is no longer there. Ads created by hand are never touched.
        /// </summary>
        public ScanResult SyncFolder(IReadOnlyList<(Guid ItemId, string Name)> libraryVideos)
        {
            var cfg = Config;
            var result = Reconcile(cfg.Advertisements, libraryVideos);

            if (result.Imported > 0 || result.RemovedMissing > 0)
            {
                Save();
            }

            _logger.LogInformation(
                "[StartupAds] Pre-roll folder scan: +{Imported} / -{Removed} (total {Total}).",
                result.Imported,
                result.RemovedMissing,
                result.Total);

            return result;
        }

        /// <summary>
        /// Pure reconciliation of the ad list against the folder's videos: adds a new
        /// <see cref="PrerollAd"/> (<see cref="PrerollAd.AutoImported"/> = true) for each library
        /// video not already referenced, and removes previously auto-imported ads whose video is no
        /// longer present. Hand-made ads are never added or removed. Mutates <paramref name="ads"/>.
        /// </summary>
        public static ScanResult Reconcile(List<PrerollAd> ads, IReadOnlyList<(Guid ItemId, string Name)> libraryVideos)
        {
            var found = new HashSet<Guid>(libraryVideos.Select(v => v.ItemId));
            var existingIds = new HashSet<Guid>();
            foreach (var ad in ads)
            {
                if (Guid.TryParse(ad.ItemId, out var g))
                {
                    existingIds.Add(g);
                }
            }

            var result = new ScanResult();

            foreach (var (id, name) in libraryVideos)
            {
                if (id == Guid.Empty || !existingIds.Add(id))
                {
                    continue;
                }

                var order = ads.Count == 0 ? 1 : ads.Max(a => a.Order) + 1;
                ads.Add(new PrerollAd
                {
                    Id = Guid.NewGuid(),
                    Name = string.IsNullOrWhiteSpace(name) ? "Pre-roll" : name,
                    ItemId = id.ToString(),
                    ItemName = name ?? string.Empty,
                    Enabled = true,
                    Priority = 5,
                    Order = order,
                    AutoImported = true
                });
                result.Imported++;
            }

            result.RemovedMissing = ads.RemoveAll(a =>
                a.AutoImported
                && (!Guid.TryParse(a.ItemId, out var g) || !found.Contains(g)));

            result.Total = ads.Count;
            return result;
        }

/// <summary>Per-ad breakdown produced by <see cref="Diagnose"/>.</summary>
        public sealed class AdDiagnosis
        {
            public string Name { get; set; } = string.Empty;

            public bool Enabled { get; set; }

            public bool VideoExists { get; set; }

            public bool UserCanSeeVideo { get; set; }

            public bool HasPlayableMedia { get; set; }

            public bool ScheduleOk { get; set; }

            public bool UserTargeted { get; set; }

            public bool WouldPlay { get; set; }

            public string Reason { get; set; } = string.Empty;
        }

        /// <summary>Result of <see cref="Diagnose"/>: a plain-language explanation of what would happen.</summary>
        public sealed class DiagnosisResult
        {
            public bool PluginEnabled { get; set; }

            public bool PrerollEnabled { get; set; }

            public bool AppliesToContentType { get; set; }

            public bool FrequencyAllowsNow { get; set; }

            public int TotalAds { get; set; }

            public int WouldPlayCount { get; set; }

            public List<AdDiagnosis> Ads { get; set; } = new();

            public string Summary { get; set; } = string.Empty;
        }

        /// <summary>
        /// Runs the exact same gates <see cref="Preroll.PrerollIntroProvider"/> uses at playback
        /// time, but returns a full explanation instead of just a yes/no — so a misconfiguration
        /// (a disabled ad, one outside its schedule, a user with no library access to the video…)
        /// can be told apart from a client-side "Modo Cine" issue without guessing.
        /// </summary>
        /// <param name="pluginCfg">The live plugin configuration.</param>
        /// <param name="itemKind">Kind of the content the viewer is about to play.</param>
        /// <param name="userId">The viewer's user id (string form).</param>
        /// <param name="nowLocal">Current local time.</param>
        /// <param name="alreadyShownToday">Whether <see cref="PrerollConfiguration.ShownLog"/> already has an entry today for this user.</param>
        /// <param name="resolveAd">
        /// For one ad's <c>ItemId</c>: (exists in a library, current user can see it, has a playable
        /// media source). Injected so this stays testable without a live <c>ILibraryManager</c>.
        /// </param>
        public static DiagnosisResult Diagnose(
            PluginConfiguration pluginCfg,
            BaseItemKind itemKind,
            string userId,
            DateTime nowLocal,
            bool alreadyShownToday,
            Func<PrerollAd, (bool Exists, bool Visible, bool HasMedia)> resolveAd)
        {
            var cfg = pluginCfg.Preroll;
            var result = new DiagnosisResult
            {
                PluginEnabled = pluginCfg.Enabled,
                PrerollEnabled = cfg.Enabled,
                TotalAds = cfg.Advertisements.Count
            };

            result.AppliesToContentType = cfg.AppliesTo switch
            {
                PrerollAppliesTo.Movies => itemKind == BaseItemKind.Movie,
                PrerollAppliesTo.Episodes => itemKind == BaseItemKind.Episode,
                _ => itemKind is BaseItemKind.Movie or BaseItemKind.Episode
            };

            result.FrequencyAllowsNow = cfg.Frequency != PrerollFrequency.OncePerDay || !alreadyShownToday;

            var gatesOpen = pluginCfg.Enabled && cfg.Enabled && result.AppliesToContentType && result.FrequencyAllowsNow;
            var selected = gatesOpen
                ? new HashSet<Guid>(Select(cfg, userId, nowLocal).Select(a => a.Id))
                : new HashSet<Guid>();

            foreach (var ad in cfg.Advertisements)
            {
                var (exists, visible, hasMedia) = string.IsNullOrWhiteSpace(ad.ItemId)
                    ? (false, false, false)
                    : resolveAd(ad);
                var scheduleOk = IsWithinSchedule(ad, nowLocal);
                var userOk = IsUserTargeted(ad, userId);
                // Select() only knows about Enabled/schedule/targeting; the library-access and
                // media-source checks (exists/visible/hasMedia) come from resolveAd and must gate
                // WouldPlay too, or a permission problem would be reported as "would play".
                var wouldPlay = gatesOpen && exists && visible && hasMedia && selected.Contains(ad.Id);

                var reason = !ad.Enabled ? "Anuncio desactivado."
                    : string.IsNullOrWhiteSpace(ad.ItemId) ? "Sin vídeo asignado."
                    : !exists ? "El vídeo ya no existe en ninguna biblioteca."
                    : !visible ? "El usuario no tiene acceso a la biblioteca que contiene ese vídeo."
                    : !hasMedia ? "El vídeo no tiene ninguna fuente de medios reproducible."
                    : !scheduleOk ? "Fuera de la fecha / día / hora programados."
                    : !userOk ? "Este anuncio no incluye a este usuario."
                    : !result.AppliesToContentType ? "«Aplicar a» no incluye este tipo de contenido."
                    : !result.FrequencyAllowsNow ? "Ya se le mostró un pre-roll hoy (frecuencia: una vez al día)."
                    : wouldPlay ? "Se reproduciría."
                    : "Descartado por el máximo por reproducción / orden / aleatorio.";

                result.Ads.Add(new AdDiagnosis
                {
                    Name = ad.Name,
                    Enabled = ad.Enabled,
                    VideoExists = exists,
                    UserCanSeeVideo = visible,
                    HasPlayableMedia = hasMedia,
                    ScheduleOk = scheduleOk,
                    UserTargeted = userOk,
                    WouldPlay = wouldPlay,
                    Reason = reason
                });
            }

            result.WouldPlayCount = result.Ads.Count(a => a.WouldPlay);

            result.Summary = !result.PluginEnabled
                ? "El interruptor general del plugin («1 · Configuración general → Activar el plugin») está apagado."
                : !result.PrerollEnabled
                    ? "El pre-roll está apagado en «3 · Anuncios antes de reproducir → Activar anuncios antes de reproducir»."
                    : !result.AppliesToContentType
                        ? "«Aplicar a» no incluye este tipo de contenido para este vídeo de prueba."
                        : !result.FrequencyAllowsNow
                            ? "La frecuencia «una vez al día» ya se cumplió hoy para este usuario."
                            : result.TotalAds == 0
                                ? "No hay ningún vídeo pre-roll creado todavía."
                                : result.WouldPlayCount > 0
                                    ? $"El servidor SÍ reproduciría {result.WouldPlayCount} vídeo(s) pre-roll aquí. Si aun así no suena nada en el dispositivo, el problema es «Modo Cine» (apagado, o el reproductor no llega a pedirlo) — no la configuración del plugin."
                                    : "Ningún vídeo pre-roll cumple todas las condiciones ahora mismo; revisa la columna «Motivo» de cada uno.";

            return result;
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
