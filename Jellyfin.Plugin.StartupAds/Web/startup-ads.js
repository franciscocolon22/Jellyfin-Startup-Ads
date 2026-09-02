/*
 * Jellyfin Startup Ads - client bootstrap
 * Injected into index.html as: <script id="startup-ads-inject" src="StartupAds/ClientScript" defer></script>
 *
 * Responsibilities:
 *  1. Wait until Jellyfin Web is initialised and a user is authenticated.
 *  2. Ask the backend which ads are active for this user.
 *  3. Render a modal/fullscreen overlay, one ad at a time.
 *  4. Run the countdown, handle skip / action / close, then clean up fully.
 *  5. Never break Jellyfin: every failure path is swallowed.
 */
(function () {
    'use strict';

    if (window.__startupAdsLoaded) {
        return;
    }
    window.__startupAdsLoaded = true;

    var STYLE_ID = 'startup-ads-style';
    var OVERLAY_ID = 'startup-ads-overlay';

    var I18N = {
        es: { skipIn: 'Omitir en', skip: 'Omitir', close: 'Cerrar', more: 'Ver más' },
        en: { skipIn: 'Skip in', skip: 'Skip', close: 'Close', more: 'Learn more' }
    };

    function log() {
        try {
            var args = ['[Jellyfin Startup Ads]'].concat([].slice.call(arguments));
            console.debug.apply(console, args);
        } catch (e) { /* noop */ }
    }

    function getApiClient() {
        if (window.ApiClient) {
            return window.ApiClient;
        }
        if (window.connectionManager && typeof window.connectionManager.currentApiClient === 'function') {
            return window.connectionManager.currentApiClient();
        }
        return null;
    }

    function currentUserId(api) {
        try {
            return api.getCurrentUserId ? api.getCurrentUserId() : null;
        } catch (e) {
            return null;
        }
    }

    function isLoggedIn(api) {
        try {
            if (typeof api.isLoggedIn === 'function') {
                return api.isLoggedIn();
            }
            return !!currentUserId(api) && !!api.accessToken();
        } catch (e) {
            return false;
        }
    }

    /* ---------------------------------------------------------------- */
    /* Bootstrap                                                         */
    /* ---------------------------------------------------------------- */
    function waitForJellyfin(cb) {
        var attempts = 0;
        var maxAttempts = 120; // ~48s at 400ms
        var timer = setInterval(function () {
            attempts++;
            var api = getApiClient();
            if (api && isLoggedIn(api)) {
                clearInterval(timer);
                cb(api);
            } else if (attempts >= maxAttempts) {
                clearInterval(timer);
                log('gave up waiting for an authenticated session');
            }
        }, 400);
    }

    function sessionKey(userId) {
        return 'startupAds:shown:' + (userId || 'anon');
    }

    function start() {
        waitForJellyfin(function (api) {
            var userId = currentUserId(api);

            fetchJson(api, 'StartupAds/Config')
                .then(function (cfg) {
                    if (!cfg || !cfg.Enabled || !cfg.Ads || !cfg.Ads.length) {
                        log('no ads to show');
                        return;
                    }

                    if (cfg.FrequencyMode === 'OncePerSession') {
                        try {
                            if (sessionStorage.getItem(sessionKey(userId)) === '1') {
                                log('already shown this session');
                                return;
                            }
                            sessionStorage.setItem(sessionKey(userId), '1');
                        } catch (e) { /* storage disabled - fall through */ }
                    }

                    injectStyle(api);
                    runQueue(api, cfg, cfg.Ads.slice());
                })
                .catch(function (err) {
                    log('config request failed', err);
                });
        });
    }

    /* React to user switching without a full reload. */
    document.addEventListener('viewshow', function onFirstView() {
        // viewshow fires on every SPA navigation; we only need the very first one
        document.removeEventListener('viewshow', onFirstView);
    });

    /* ---------------------------------------------------------------- */
    /* Networking                                                        */
    /* ---------------------------------------------------------------- */
    function fetchJson(api, path) {
        var url = api.getUrl(path);
        if (typeof api.ajax === 'function') {
            return api.ajax({ type: 'GET', url: url, dataType: 'json' });
        }
        return fetch(url, {
            headers: { 'X-Emby-Token': api.accessToken() }
        }).then(function (r) {
            if (!r.ok) { throw new Error('HTTP ' + r.status); }
            return r.json();
        });
    }

    function postTrack(api, adId, kind) {
        try {
            var url = api.getUrl('StartupAds/Track/' + adId + '/' + kind);
            if (typeof api.ajax === 'function') {
                api.ajax({ type: 'POST', url: url });
            } else {
                fetch(url, { method: 'POST', headers: { 'X-Emby-Token': api.accessToken() } });
            }
        } catch (e) { /* analytics are best-effort */ }
    }

    function mediaUrl(api, relative) {
        return api.getUrl(relative, { api_key: api.accessToken() });
    }

    /* ---------------------------------------------------------------- */
    /* Rendering                                                         */
    /* ---------------------------------------------------------------- */
    function injectStyle(api) {
        if (document.getElementById(STYLE_ID)) {
            return;
        }
        var link = document.createElement('link');
        link.id = STYLE_ID;
        link.rel = 'stylesheet';
        link.href = api.getUrl('StartupAds/ClientStyle');
        document.head.appendChild(link);
    }

    function runQueue(api, cfg, queue) {
        if (!queue.length) {
            return;
        }
        var ad = queue.shift();
        showAd(api, cfg, ad, function () {
            runQueue(api, cfg, queue);
        });
    }

    function t(cfg, key) {
        var lang = (cfg.Language || 'es').toLowerCase();
        return (I18N[lang] || I18N.es)[key];
    }

    function showAd(api, cfg, ad, done) {
        // Guard against a duplicate overlay (defensive - SPA re-entrancy).
        var existing = document.getElementById(OVERLAY_ID);
        if (existing) {
            existing.parentNode.removeChild(existing);
        }

        var cleanupFns = [];
        var finished = false;

        function cleanup(reason) {
            if (finished) { return; }
            finished = true;
            cleanupFns.forEach(function (fn) {
                try { fn(); } catch (e) { /* noop */ }
            });
            var node = document.getElementById(OVERLAY_ID);
            if (node) {
                node.classList.add('sa-hide');
                setTimeout(function () {
                    if (node.parentNode) { node.parentNode.removeChild(node); }
                }, 220);
            }
            log('ad dismissed:', reason);
            done();
        }

        var overlay = document.createElement('div');
        overlay.id = OVERLAY_ID;
        overlay.className = 'sa-overlay sa-mode-' + String(cfg.DisplayMode || 'Modal').toLowerCase();
        overlay.setAttribute('role', 'dialog');
        overlay.setAttribute('aria-modal', 'true');
        if (ad.Title) {
            overlay.setAttribute('aria-label', ad.Title);
        }
        overlay.style.setProperty('--sa-overlay-opacity', String(cfg.OverlayOpacity != null ? cfg.OverlayOpacity : 0.85));
        overlay.style.setProperty('--sa-accent', cfg.AccentColor || '#00a4dc');

        var card = document.createElement('div');
        card.className = 'sa-card';
        card.style.maxWidth = (cfg.MaxWidthPx || 900) + 'px';
        card.style.maxHeight = (cfg.MaxHeightPx || 700) + 'px';
        card.style.borderRadius = (cfg.BorderRadiusPx != null ? cfg.BorderRadiusPx : 14) + 'px';

        if (ad.BackgroundUrl) {
            card.style.backgroundImage = 'url("' + mediaUrl(api, ad.BackgroundUrl) + '")';
            card.classList.add('sa-has-bg');
        }

        /* ---- media ---- */
        var mediaWrap = document.createElement('div');
        mediaWrap.className = 'sa-media';
        var videoEl = null;

        var type = String(ad.Type || 'Image');
        if ((type === 'Video' || (type === 'Multimedia' && ad.MediaUrl)) && ad.MediaUrl) {
            videoEl = document.createElement('video');
            videoEl.className = 'sa-video';
            videoEl.src = mediaUrl(api, ad.MediaUrl);
            videoEl.style.objectFit = ad.ObjectFit || 'contain';
            videoEl.playsInline = true;
            videoEl.autoplay = !!cfg.AutoplayVideo;
            videoEl.muted = !!cfg.MutedVideo;
            videoEl.loop = !!cfg.LoopVideo;
            videoEl.controls = !!cfg.ShowVideoControls;
            videoEl.preload = 'auto';
            mediaWrap.appendChild(videoEl);
        } else if ((type === 'Image' || type === 'Multimedia') && ad.MediaUrl) {
            var img = document.createElement('img');
            img.className = 'sa-image';
            img.alt = ad.Title || '';
            img.src = mediaUrl(api, ad.MediaUrl);
            img.style.objectFit = ad.ObjectFit || 'contain';
            mediaWrap.appendChild(img);
        }
        if (mediaWrap.childNodes.length) {
            card.appendChild(mediaWrap);
        }

        /* ---- text ---- */
        var body = document.createElement('div');
        body.className = 'sa-body';
        if (ad.Title) {
            var h = document.createElement('h2');
            h.className = 'sa-title';
            h.textContent = ad.Title; // textContent => no XSS
            body.appendChild(h);
        }
        if (ad.Description) {
            var p = document.createElement('p');
            p.className = 'sa-desc';
            p.textContent = ad.Description;
            body.appendChild(p);
        }

        /* ---- action button ---- */
        if (ad.ButtonText && ad.ButtonAction && ad.ButtonAction !== 'None') {
            var btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'sa-btn sa-btn-action';
            btn.textContent = ad.ButtonText;
            btn.addEventListener('click', function () {
                postTrack(api, ad.Id, 'clicked');
                handleAction(api, ad);
                if (ad.ButtonAction !== 'ExternalUrl') {
                    cleanup('action');
                }
            });
            body.appendChild(btn);
        }
        card.appendChild(body);

        /* ---- footer: countdown + skip ---- */
        var footer = document.createElement('div');
        footer.className = 'sa-footer';

        var skipBtn = document.createElement('button');
        skipBtn.type = 'button';
        skipBtn.className = 'sa-btn sa-skip';
        footer.appendChild(skipBtn);

        card.appendChild(footer);

        /* ---- close (X) ---- */
        if (cfg.ShowCloseButton) {
            var x = document.createElement('button');
            x.type = 'button';
            x.className = 'sa-close';
            x.setAttribute('aria-label', t(cfg, 'close'));
            x.textContent = '×';
            x.addEventListener('click', function () {
                postTrack(api, ad.Id, 'skipped');
                cleanup('close-x');
            });
            card.appendChild(x);
        }

        overlay.appendChild(card);
        document.body.appendChild(overlay);
        requestAnimationFrame(function () { overlay.classList.add('sa-show'); });
        postTrack(api, ad.Id, 'shown');

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

            if (canSkip) {
                skipBtn.textContent = t(cfg, 'skip');
                skipBtn.disabled = false;
                skipBtn.classList.add('sa-ready');
                skipBtn.hidden = false;
            } else if (!allowSkip) {
                skipBtn.hidden = true;
            } else if (cfg.SkipButtonMode === 'AppearsAfterCountdown' && elapsed < skipAfter) {
                skipBtn.hidden = true;
            } else {
                // disabled-until-countdown
                skipBtn.hidden = false;
                skipBtn.disabled = true;
                skipBtn.classList.remove('sa-ready');
                var secsToSkip = Math.max(0, Math.ceil(skipAfter - elapsed));
                skipBtn.textContent = ad.ShowCountdown
                    ? t(cfg, 'skipIn') + ' ' + secsToSkip
                    : t(cfg, 'skip');
            }

            if (ad.ShowCountdown && cfg.ShowCountdown && !canSkip && remaining > 0) {
                skipBtn.setAttribute('data-remaining', remaining);
            }
        }

        skipBtn.addEventListener('click', function () {
            if (skipBtn.disabled) { return; }
            postTrack(api, ad.Id, 'skipped');
            cleanup('skip');
        });

        function beginTimers() {
            renderFooter();
            tickTimer = setInterval(renderFooter, 250);
            endTimer = setTimeout(function () {
                postTrack(api, ad.Id, 'completed');
                if (allowSkip) {
                    renderFooter(); // leave the overlay up with an enabled Skip
                } else {
                    cleanup('duration-elapsed');
                }
            }, totalDuration * 1000);
        }

        if (videoEl && ad.UseVideoDuration) {
            var startedWithMeta = false;
            videoEl.addEventListener('loadedmetadata', function () {
                if (startedWithMeta) { return; }
                startedWithMeta = true;
                if (isFinite(videoEl.duration) && videoEl.duration > 0) {
                    totalDuration = Math.ceil(videoEl.duration);
                }
                beginTimers();
            });
            videoEl.addEventListener('ended', function () {
                if (!cfg.LoopVideo) {
                    postTrack(api, ad.Id, 'completed');
                    if (!allowSkip) { cleanup('video-ended'); } else { renderFooter(); }
                }
            });
            videoEl.addEventListener('error', function () {
                log('video failed to load, falling back to manual duration');
                if (!startedWithMeta) { startedWithMeta = true; beginTimers(); }
            });
            // Safety net if metadata never arrives.
            setTimeout(function () {
                if (!startedWithMeta) { startedWithMeta = true; beginTimers(); }
            }, 4000);
        } else {
            beginTimers();
        }

        if (videoEl && cfg.AutoplayVideo) {
            var playPromise = videoEl.play();
            if (playPromise && playPromise.catch) {
                playPromise.catch(function () {
                    // Autoplay blocked - retry muted.
                    videoEl.muted = true;
                    videoEl.play().catch(function () { log('video autoplay blocked'); });
                });
            }
        }

        /* ---- keyboard ---- */
        if (cfg.AllowCloseWithEscape) {
            var onKey = function (ev) {
                if (ev.key === 'Escape') {
                    var elapsed = (Date.now() - startTs) / 1000;
                    if (!allowSkip || elapsed >= skipAfter) {
                        postTrack(api, ad.Id, 'skipped');
                        cleanup('escape');
                    }
                }
            };
            document.addEventListener('keydown', onKey, true);
            cleanupFns.push(function () { document.removeEventListener('keydown', onKey, true); });
        }

        // Focus management for accessibility.
        setTimeout(function () {
            try { (skipBtn.hidden ? card : skipBtn).focus(); } catch (e) { /* noop */ }
        }, 50);
    }

    /* ---------------------------------------------------------------- */
    /* Button actions                                                    */
    /* ---------------------------------------------------------------- */
    function handleAction(api, ad) {
        try {
            if (ad.ButtonAction === 'ExternalUrl' && ad.ButtonUrl) {
                window.open(ad.ButtonUrl, '_blank', 'noopener,noreferrer');
            } else if (ad.ButtonAction === 'JellyfinItem' && ad.ButtonItemId) {
                var target = 'details?id=' + encodeURIComponent(ad.ButtonItemId) +
                    '&serverId=' + encodeURIComponent(api.serverId ? api.serverId() : '');
                if (window.Dashboard && typeof window.Dashboard.navigate === 'function') {
                    window.Dashboard.navigate(target);
                } else {
                    window.location.hash = '#/' + target;
                }
            }
        } catch (e) {
            log('action failed', e);
        }
    }

    /* ---------------------------------------------------------------- */
    start();
})();
