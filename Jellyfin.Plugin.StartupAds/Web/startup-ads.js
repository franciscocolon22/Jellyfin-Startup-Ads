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
        overlay.__saCleanup = cleanup;

        var card = document.createElement('div');
        card.className = NS + '-card';
        card.style.maxWidth = (cfg.MaxWidthPx || 900) + 'px';
        card.style.maxHeight = (cfg.MaxHeightPx || 700) + 'px';
        card.style.borderRadius = (cfg.BorderRadiusPx != null ? cfg.BorderRadiusPx : 14) + 'px';
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

        if ((type === 'Video' || type === 'Multimedia') && ad.MediaUrl && looksVideo(ad)) {
            videoEl = document.createElement('video');
            videoEl.className = NS + '-video';
            videoEl.src = mediaUrl(api, ad.MediaUrl);
            videoEl.style.objectFit = ad.ObjectFit || 'contain';
            videoEl.playsInline = true;
            videoEl.autoplay = cfg.AutoplayVideo !== false;
            videoEl.muted = cfg.MutedVideo !== false;
            videoEl.loop = !!cfg.LoopVideo;
            videoEl.controls = !!cfg.ShowVideoControls;
            videoEl.preload = 'auto';
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

        /* ---- countdown / skip state machine ---- */
        var allowSkip = !!ad.AllowSkip;
        var skipAfter = Math.max(0, ad.SkipAfterSeconds || 0);
        var manualDuration = Math.max(1, ad.DurationSeconds || cfg.DefaultDurationSeconds || 10);
        var totalDuration = manualDuration;
        var startTs = Date.now();
        var tickTimer = null;
        var endTimer = null;

        function clearTimers() {
            if (tickTimer) { clearInterval(tickTimer); tickTimer = null; }
            if (endTimer) { clearTimeout(endTimer); endTimer = null; }
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
            var remaining = Math.max(0, Math.ceil(totalDuration - elapsed));
            var canSkip = allowSkip && elapsed >= skipAfter;

            if (!allowSkip) {
                skipBtn.hidden = true;
                return;
            }

            if (canSkip) {
                skipBtn.hidden = false;
                skipBtn.disabled = false;
                skipBtn.classList.add(NS + '-ready');
                skipBtn.textContent = t(cfg, 'skip');
                return;
            }

            if (cfg.SkipButtonMode === 'AppearsAfterCountdown') {
                skipBtn.hidden = true;
                return;
            }

            // DisabledUntilCountdown
            skipBtn.hidden = false;
            skipBtn.disabled = true;
            skipBtn.classList.remove(NS + '-ready');
            var secs = Math.max(0, Math.ceil(skipAfter - elapsed));
            skipBtn.textContent = (ad.ShowCountdown && cfg.ShowCountdown)
                ? t(cfg, 'skipIn') + ' ' + secs
                : t(cfg, 'skip');
            void remaining;
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
        }

        if (videoEl && ad.UseVideoDuration) {
            var started = false;
            var begin = function () { if (!started) { started = true; beginTimers(); } };
            videoEl.addEventListener('loadedmetadata', function () {
                if (isFinite(videoEl.duration) && videoEl.duration > 0) {
                    totalDuration = Math.ceil(videoEl.duration);
                }
                begin();
            });
            videoEl.addEventListener('ended', function () {
                if (!cfg.LoopVideo) { finishByDuration('video-ended'); }
            });
            videoEl.addEventListener('error', function () {
                log('video failed to load; using manual duration');
                begin();
            });
            setTimeout(begin, 4000); // safety net if metadata never arrives
        } else {
            beginTimers();
        }

        if (videoEl) {
            videoEl.addEventListener('playing', function () { trackOnce('started'); }, { once: true });
            if (cfg.AutoplayVideo !== false) {
                var pr = videoEl.play();
                if (pr && pr.catch) {
                    pr.catch(function () {
                        videoEl.muted = true;
                        videoEl.play().catch(function () { log('video autoplay blocked'); });
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
        if (ad.Type === 'Video') { return true; }
        if (ad.Type !== 'Multimedia' || !ad.MediaUrl) { return false; }
        return /\.(mp4|webm|m4v|mov|ogv|ogg)(\?|$)/i.test(ad.MediaUrl) || ad.UseVideoDuration === true;
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
