/*
 * Jellyfin Startup Ads - client bootstrap (Jellyfin Web 10.11.x)
 *
 * Injected as: <script id="startup-ads-inject" src="StartupAds/ClientScript" defer></script>
 *
 * Flow:
 *   wait for an authenticated ApiClient  ->  GET StartupAds/Config for THIS user
 *   ->  render one overlay per ad in sequence  ->  countdown / skip / action  ->  full cleanup
 *
 * Guarantees:
 *   - one bootstrap per full page load (window.__startupAdsLoaded)
 *   - re-evaluates on user login / user switch / logout+login (no infinite polling)
 *   - never more than one overlay in the DOM
 *   - exactly one "completed" per ad view even if timeout + video "ended" race
 *   - Jellyfin keeps working if anything here throws
 */
(function () {
    'use strict';

    if (window.__startupAdsLoaded) {
        return;
    }
    window.__startupAdsLoaded = true;

    var NS = 'startup-ads';
    var STYLE_ID = NS + '-style';
    var OVERLAY_ID = NS + '-overlay';

    var I18N = {
        es: { skipIn: 'Omitir en', skip: 'Omitir', close: 'Cerrar' },
        en: { skipIn: 'Skip in', skip: 'Skip', close: 'Close' }
    };

    var state = {
        lastUserId: null,   // user for whom we already evaluated on this page load
        running: false,      // an overlay/queue is currently active
        pollTimer: null,
        pollAttempts: 0
    };

    function log() {
        try {
            console.debug.apply(console, ['[Jellyfin Startup Ads]'].concat([].slice.call(arguments)));
        } catch (e) { /* noop */ }
    }

    function getApiClient() {
        if (window.ApiClient && typeof window.ApiClient.getUrl === 'function') {
            return window.ApiClient;
        }
        if (window.connectionManager && typeof window.connectionManager.currentApiClient === 'function') {
            try { return window.connectionManager.currentApiClient(); } catch (e) { return null; }
        }
        return null;
    }

    function currentUserId(api) {
        try { return (api && api.getCurrentUserId && api.getCurrentUserId()) || null; }
        catch (e) { return null; }
    }

    function isLoggedIn(api) {
        if (!api) { return false; }
        try {
            if (typeof api.isLoggedIn === 'function') { return api.isLoggedIn(); }
            return !!currentUserId(api) && !!api.accessToken();
        } catch (e) { return false; }
    }

    function t(cfg, key) {
        var lang = (cfg && cfg.Language ? cfg.Language : 'es').toLowerCase();
        return (I18N[lang] || I18N.es)[key];
    }

    /* ----------------------------------------------------------------
     * Bootstrap / user lifecycle
     * ---------------------------------------------------------------- */
    function startPolling() {
        if (state.pollTimer) { return; }
        state.pollAttempts = 0;
        state.pollTimer = setInterval(function () {
            state.pollAttempts++;
            var api = getApiClient();
            if (api && isLoggedIn(api)) {
                stopPolling();
                evaluate();
            } else if (state.pollAttempts >= 40) { // ~20s
                stopPolling();
                log('no authenticated session after initial wait; will react to navigation events');
            }
        }, 500);
    }

    function stopPolling() {
        if (state.pollTimer) {
            clearInterval(state.pollTimer);
            state.pollTimer = null;
        }
    }

    /* Runs on load and on every SPA navigation. Cheap unless the user actually changed. */
    function evaluate() {
        var api = getApiClient();

        if (!api || !isLoggedIn(api)) {
            // Logged out: forget the last user so a future login re-triggers.
            state.lastUserId = null;
            return;
        }

        var uid = currentUserId(api);
        if (!uid || uid === state.lastUserId) {
            return;
        }

        // User switched (or first evaluation): drop any overlay meant for the previous user.
        state.lastUserId = uid;
        forceCloseOverlay('user-change');

        fetchJson(api, 'StartupAds/Config')
            .then(function (cfg) {
                if (!cfg || !cfg.Enabled || !cfg.Ads || !cfg.Ads.length) {
                    log('no ads for user', uid);
                    return;
                }

                if (cfg.FrequencyMode === 'OncePerSession') {
                    var key = 'startupAds:shown:' + uid;
                    try {
                        if (sessionStorage.getItem(key) === '1') {
                            log('OncePerSession: already shown for', uid);
                            return;
                        }
                        sessionStorage.setItem(key, '1');
                    } catch (e) { /* storage unavailable -> behave like EveryStartup */ }
                }

                injectStyle(api);
                runQueue(api, cfg, cfg.Ads.slice(), uid);
            })
            .catch(function (err) { log('config request failed', err); });
    }

    document.addEventListener('viewshow', evaluate, true);
    window.addEventListener('pagehide', function () { forceCloseOverlay('pagehide'); });

    /* ----------------------------------------------------------------
     * Networking
     * ---------------------------------------------------------------- */
    function fetchJson(api, path) {
        var url = api.getUrl(path);
        if (typeof api.ajax === 'function') {
            // ApiClient.ajax builds the proper "Authorization: MediaBrowser Token=..." header.
            return api.ajax({ type: 'GET', url: url, dataType: 'json' });
        }
        return fetch(url, { headers: { 'Authorization': authHeader(api) } })
            .then(function (r) {
                if (!r.ok) { throw new Error('HTTP ' + r.status); }
                return r.json();
            });
    }

    function authHeader(api) {
        return 'MediaBrowser Token="' + api.accessToken() + '", Client="Jellyfin Web", Device="StartupAds", DeviceId="startup-ads", Version="1.0"';
    }

    function postTrack(api, adId, kind) {
        try {
            var url = api.getUrl('StartupAds/Track/' + adId + '/' + kind);
            if (typeof api.ajax === 'function') {
                api.ajax({ type: 'POST', url: url });
            } else {
                fetch(url, { method: 'POST', headers: { 'Authorization': authHeader(api) } });
            }
        } catch (e) { /* analytics are best-effort */ }
    }

    function mediaUrl(api, relative) {
        // "ApiKey" (capitalised) is honoured regardless of the server's legacy-auth setting.
        return api.getUrl(relative, { ApiKey: api.accessToken() });
    }

    /* ----------------------------------------------------------------
     * Rendering
     * ---------------------------------------------------------------- */
    function injectStyle(api) {
        if (document.getElementById(STYLE_ID)) { return; }
        var link = document.createElement('link');
        link.id = STYLE_ID;
        link.rel = 'stylesheet';
        link.href = api.getUrl('StartupAds/ClientStyle');
        document.head.appendChild(link);
    }

    function forceCloseOverlay(reason) {
        var node = document.getElementById(OVERLAY_ID);
        if (node && node.__saCleanup) {
            node.__saCleanup(reason);
        } else if (node && node.parentNode) {
            node.parentNode.removeChild(node);
        }
        state.running = false;
    }

    function runQueue(api, cfg, queue, uid) {
        if (uid !== state.lastUserId) { return; } // user changed while queue was pending
        if (!queue.length) { state.running = false; return; }
        state.running = true;
        var ad = queue.shift();
        showAd(api, cfg, ad, function () {
            runQueue(api, cfg, queue, uid);
        });
    }

    function showAd(api, cfg, ad, done) {
        forceCloseOverlay('replaced');

        var cleanupFns = [];
        var finished = false;
        var completionTracked = false;
        var impressionTracked = false;
        var lastFocused = document.activeElement;

        function trackOnce(kind) {
            if (kind === 'completed') {
                if (completionTracked) { return; }
                completionTracked = true;
            }
            if (kind === 'impression') {
                if (impressionTracked) { return; }
                impressionTracked = true;
            }
            postTrack(api, ad.Id, kind);
        }

        function cleanup(reason) {
            if (finished) { return; }
            finished = true;
            cleanupFns.forEach(function (fn) { try { fn(); } catch (e) { /* noop */ } });
            var node = document.getElementById(OVERLAY_ID);
            if (node) {
                node.__saCleanup = null;
                node.classList.add(NS + '-hide');
                setTimeout(function () {
                    if (node.parentNode) { node.parentNode.removeChild(node); }
                }, 220);
            }
            try { if (lastFocused && lastFocused.focus) { lastFocused.focus(); } } catch (e) { /* noop */ }
            log('ad dismissed:', reason);
            done();
        }

        var overlay = document.createElement('div');
        overlay.id = OVERLAY_ID;
        overlay.className = NS + '-overlay ' + NS + '-mode-' + String(cfg.DisplayMode || 'Modal').toLowerCase();
        overlay.setAttribute('role', 'dialog');
        overlay.setAttribute('aria-modal', 'true');
        overlay.setAttribute('aria-label', ad.Title || 'Anuncio');
        overlay.style.setProperty('--sa-overlay-opacity', String(cfg.OverlayOpacity != null ? cfg.OverlayOpacity : 0.85));
        overlay.style.setProperty('--sa-accent', cfg.AccentColor || '#00a4dc');
        // Sizing goes through CSS custom properties so the per-mode rules
        // (fullscreen / center banner) can override them. Setting them as inline
        // styles on the card would always win and every mode would look "modal".
        overlay.style.setProperty('--sa-max-w', (cfg.MaxWidthPx || 900) + 'px');
        overlay.style.setProperty('--sa-max-h', (cfg.MaxHeightPx || 700) + 'px');
        overlay.style.setProperty('--sa-radius', (cfg.BorderRadiusPx != null ? cfg.BorderRadiusPx : 14) + 'px');
        overlay.__saCleanup = cleanup;

        var card = document.createElement('div');
        card.className = NS + '-card';
        card.tabIndex = -1;

        if (ad.BackgroundUrl) {
            card.style.backgroundImage = 'url("' + mediaUrl(api, ad.BackgroundUrl) + '")';
            card.classList.add(NS + '-has-bg');
        }

        /* media */
        var mediaWrap = document.createElement('div');
        mediaWrap.className = NS + '-media';
        var videoEl = null;
        var type = String(ad.Type || 'Image');

        var wantMuted = cfg.MutedVideo !== false;
        if ((type === 'Video' || type === 'Multimedia') && ad.MediaUrl && looksVideo(ad)) {
            videoEl = document.createElement('video');
            videoEl.className = NS + '-video';
            // Muting must be set BEFORE src/insertion, and via the attribute +
            // defaultMuted, or Chrome/Safari ignore it for the autoplay decision.
            if (wantMuted) {
                videoEl.muted = true;
                videoEl.defaultMuted = true;
                videoEl.setAttribute('muted', '');
            }
            videoEl.playsInline = true;
            videoEl.setAttribute('playsinline', '');
            videoEl.loop = !!cfg.LoopVideo;
            videoEl.controls = !!cfg.ShowVideoControls;
            videoEl.preload = 'auto';
            videoEl.style.objectFit = ad.ObjectFit || 'contain';
            videoEl.autoplay = cfg.AutoplayVideo !== false;
            if (cfg.AutoplayVideo !== false) { videoEl.setAttribute('autoplay', ''); }
            videoEl.src = mediaUrl(api, ad.MediaUrl);
            mediaWrap.appendChild(videoEl);
        } else if ((type === 'Image' || type === 'Multimedia') && ad.MediaUrl) {
            var img = document.createElement('img');
            img.className = NS + '-image';
            img.alt = ad.Title || '';
            img.src = mediaUrl(api, ad.MediaUrl);
            img.style.objectFit = ad.ObjectFit || 'contain';
            mediaWrap.appendChild(img);
        }
        if (mediaWrap.childNodes.length) {
            card.appendChild(mediaWrap);
        }

        /* text */
        var body = document.createElement('div');
        body.className = NS + '-body';
        if (ad.Title) {
            var h = document.createElement('h2');
            h.className = NS + '-title';
            h.textContent = ad.Title;            // textContent => no HTML injection
            body.appendChild(h);
        }
        if (ad.Description) {
            var p = document.createElement('p');
            p.className = NS + '-desc';
            p.textContent = ad.Description;
            body.appendChild(p);
        }

        /* action button */
        var actionBtn = null;
        if (ad.ButtonText && ad.ButtonAction && ad.ButtonAction !== 'None') {
            actionBtn = document.createElement('button');
            actionBtn.type = 'button';
            actionBtn.className = NS + '-btn ' + NS + '-btn-action';
            actionBtn.textContent = ad.ButtonText;
            actionBtn.addEventListener('click', function () {
                trackOnce('clicked');
                var keepOpen = handleAction(api, ad);
                if (!keepOpen) { cleanup('action'); }
            });
            body.appendChild(actionBtn);
        }
        card.appendChild(body);

        /* footer: countdown + skip */
        var footer = document.createElement('div');
        footer.className = NS + '-footer';
        var skipBtn = document.createElement('button');
        skipBtn.type = 'button';
        skipBtn.className = NS + '-btn ' + NS + '-skip';
        skipBtn.setAttribute('aria-live', 'polite');
        footer.appendChild(skipBtn);
        card.appendChild(footer);

        /* close (X) */
        var closeBtn = null;
        if (cfg.ShowCloseButton) {
            closeBtn = document.createElement('button');
            closeBtn.type = 'button';
            closeBtn.className = NS + '-close';
            closeBtn.setAttribute('aria-label', t(cfg, 'close'));
            closeBtn.textContent = '×';
            closeBtn.addEventListener('click', function () {
                trackOnce('skipped');
                cleanup('close-x');
            });
            card.appendChild(closeBtn);
        }

        overlay.appendChild(card);
        document.body.appendChild(overlay);
        requestAnimationFrame(function () { overlay.classList.add(NS + '-show'); });
        trackOnce('impression');

        /* ---- countdown / skip state machine ----
           ONE global time ("Duración del anuncio", cfg.DefaultDurationSeconds) is the countdown
           for EVERY ad type. It shows N -> 0. At 0: if skipping is allowed the "Omitir" button
           turns active and the overlay waits for the user; if not, the overlay closes.
           A video longer than N is simply cut off; a shorter video ends and the ad waits. */
        var allowSkip = !!ad.AllowSkip;
        var totalDuration = Math.max(1, ad.DurationSeconds || cfg.DefaultDurationSeconds || 10);
        var skipAfter = totalDuration;   // skip enables exactly when the countdown reaches 0
        var startTs = Date.now();
        var tickTimer = null;
        var endTimer = null;
        var safetyTimer = null;

        function clearTimers() {
            if (tickTimer) { clearInterval(tickTimer); tickTimer = null; }
            if (endTimer) { clearTimeout(endTimer); endTimer = null; }
            if (safetyTimer) { clearTimeout(safetyTimer); safetyTimer = null; }
        }
        cleanupFns.push(clearTimers);
        cleanupFns.push(function () {
            if (videoEl) {
                try {
                    videoEl.pause();
                    videoEl.removeAttribute('src');
                    videoEl.load();
                } catch (e) { /* noop */ }
            }
        });

        function renderFooter() {
            var elapsed = (Date.now() - startTs) / 1000;
            // The countdown reflects the AD DURATION (10 -> 0).
            var remaining = Math.max(0, Math.ceil(totalDuration - elapsed));
            var showCountdown = ad.ShowCountdown && cfg.ShowCountdown;
            var canSkip = allowSkip && (remaining <= 0 || elapsed >= skipAfter);

            skipBtn.classList.remove(NS + '-ready');

            if (canSkip) {
                skipBtn.hidden = false;
                skipBtn.disabled = false;
                skipBtn.classList.add(NS + '-ready');
                skipBtn.textContent = t(cfg, 'skip');
                return;
            }

            // Cannot skip yet (or skipping disabled for this ad).
            var appearsMode = allowSkip && cfg.SkipButtonMode === 'AppearsAfterCountdown';

            if (!allowSkip || appearsMode) {
                // No usable button right now. Show a bare countdown if enabled, else nothing.
                if (showCountdown) {
                    skipBtn.hidden = false;
                    skipBtn.disabled = true;
                    skipBtn.textContent = t(cfg, 'skipIn') + ' ' + remaining;
                } else {
                    skipBtn.hidden = true;
                }
                return;
            }

            // allowSkip && DisabledUntilCountdown && not skippable yet.
            skipBtn.hidden = false;
            skipBtn.disabled = true;
            skipBtn.textContent = showCountdown
                ? t(cfg, 'skipIn') + ' ' + remaining
                : t(cfg, 'skip');
        }

        skipBtn.addEventListener('click', function () {
            if (skipBtn.disabled || skipBtn.hidden) { return; }
            trackOnce('skipped');
            cleanup('skip');
        });

        function finishByDuration(reason) {
            trackOnce('completed');
            if (allowSkip) {
                renderFooter();       // keep overlay up with an enabled Skip
            } else {
                cleanup(reason);
            }
        }

        function beginTimers() {
            renderFooter();
            tickTimer = setInterval(renderFooter, 250);
            endTimer = setTimeout(function () { finishByDuration('duration-elapsed'); }, totalDuration * 1000);
            // A skippable ad waits for the user after the countdown. Safety net so it can
            // never block Jellyfin forever if the user walks away.
            if (allowSkip) {
                safetyTimer = setTimeout(function () { cleanup('safety-timeout'); },
                    (totalDuration + 300) * 1000);
            }
        }

        beginTimers();

        if (videoEl) {
            // If the clip ends before the countdown, close (or keep waiting for Omitir).
            videoEl.addEventListener('ended', function () {
                if (!cfg.LoopVideo) { finishByDuration('video-ended'); }
            });
            videoEl.addEventListener('error', function () { log('video failed to load'); });
            videoEl.addEventListener('playing', function () { trackOnce('started'); }, { once: true });
            if (cfg.AutoplayVideo !== false) {
                var pr = videoEl.play();
                if (pr && pr.catch) {
                    pr.catch(function () {
                        if (wantMuted) {
                            // Should not happen (already muted) - retry once.
                            videoEl.muted = true;
                            videoEl.play().catch(function () { log('video autoplay blocked'); });
                        } else {
                            // The admin asked for sound but the browser blocks unmuted
                            // autoplay. Expose controls so the user can start it.
                            videoEl.controls = true;
                            log('unmuted autoplay blocked by the browser; showing controls');
                        }
                    });
                }
            }
        } else {
            trackOnce('started');
        }

        /* keyboard: ESC + focus trap */
        var onKey = function (ev) {
            if (ev.key === 'Escape' && cfg.AllowCloseWithEscape) {
                var elapsed = (Date.now() - startTs) / 1000;
                if (!allowSkip || elapsed >= skipAfter) {
                    trackOnce('skipped');
                    cleanup('escape');
                }
                return;
            }
            if (ev.key === 'Tab') {
                var focusables = card.querySelectorAll('button:not([hidden]):not([disabled])');
                if (!focusables.length) { ev.preventDefault(); card.focus(); return; }
                var first = focusables[0];
                var last = focusables[focusables.length - 1];
                if (ev.shiftKey && document.activeElement === first) { ev.preventDefault(); last.focus(); }
                else if (!ev.shiftKey && document.activeElement === last) { ev.preventDefault(); first.focus(); }
            }
        };
        document.addEventListener('keydown', onKey, true);
        cleanupFns.push(function () { document.removeEventListener('keydown', onKey, true); });

        setTimeout(function () {
            try {
                var target = (actionBtn && !actionBtn.hidden) ? actionBtn
                    : (!skipBtn.hidden ? skipBtn : (closeBtn || card));
                target.focus();
            } catch (e) { /* noop */ }
        }, 50);
    }

    function looksVideo(ad) {
        if (ad.MediaKind === 'video') { return true; }
        if (ad.MediaKind === 'image') { return false; }
        if (ad.Type === 'Video') { return true; }
        return /\.(mp4|webm|m4v|mov|ogv|ogg)(\?|$)/i.test(ad.MediaUrl || '');
    }

    /* ----------------------------------------------------------------
     * Button actions. Returns true to keep the overlay open.
     * ---------------------------------------------------------------- */
    function handleAction(api, ad) {
        try {
            if (ad.ButtonAction === 'ExternalUrl' && /^https?:\/\//i.test(ad.ButtonUrl || '')) {
                window.open(ad.ButtonUrl, '_blank', 'noopener,noreferrer');
                return true; // external tab; leave the overlay so the user can still skip
            }
            if (ad.ButtonAction === 'JellyfinItem' && ad.ButtonItemId) {
                var serverId = api.serverId ? api.serverId() : '';
                var target = 'details?id=' + encodeURIComponent(ad.ButtonItemId) +
                    (serverId ? '&serverId=' + encodeURIComponent(serverId) : '');
                if (window.Dashboard && typeof window.Dashboard.navigate === 'function') {
                    window.Dashboard.navigate(target);
                } else {
                    window.location.hash = '#/' + target;
                }
                return false;
            }
        } catch (e) {
            log('action failed', e);
        }
        return false; // CloseOnly and everything else
    }

    /* ---------------------------------------------------------------- */
    startPolling();
    evaluate();
})();
