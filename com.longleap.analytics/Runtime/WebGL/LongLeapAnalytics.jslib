mergeInto(LibraryManager.library, {
    // Queues a POST that the browser delivers even if the page is closing.
    //
    // The body goes as a plain string, i.e. text/plain: a CORS "simple"
    // request, which sendBeacon requires (it cannot do a preflight). GA4
    // parses the JSON regardless of the content type.
    //
    // Returns 1 only when the browser queued the request, 0 when it refused
    // (beacon quota of ~64 KB in flight exceeded) or sendBeacon is missing.
    // There is deliberately no fetch() fallback: fetch with keepalive shares
    // the same quota and fails asynchronously, after this function would have
    // had to report success.
    LongLeapAnalyticsSendBeacon: function (urlPtr, bodyPtr) {
        var url = UTF8ToString(urlPtr);
        var body = UTF8ToString(bodyPtr);

        try {
            if (navigator.sendBeacon && navigator.sendBeacon(url, body)) {
                return 1;
            }
        } catch (e) {
            // Treated as refused.
        }

        return 0;
    },

    // Reports page visibility by calling callback(id, hidden): hidden = 1 when
    // the tab is hidden or the page is being unloaded, 0 when it is shown
    // again (including a back/forward cache restore). Duplicate states are
    // filtered. Listeners are kept per id so that
    // LongLeapAnalyticsUnwatchVisibility can remove them.
    LongLeapAnalyticsWatchVisibility: function (id, callback) {
        var watchers = Module.longLeapAnalyticsWatchers || (Module.longLeapAnalyticsWatchers = {});
        if (watchers[id]) {
            return;
        }

        var state = { hidden: document.visibilityState === 'hidden' };

        function report(isHidden) {
            if (state.hidden === isHidden) {
                return;
            }
            state.hidden = isHidden;
            {{{ makeDynCall('vii', 'callback') }}}(id, isHidden ? 1 : 0);
        }

        var handlers = {
            visibilitychange: function () { report(document.visibilityState === 'hidden'); },
            pagehide: function () { report(true); },
            pageshow: function () { report(document.visibilityState === 'hidden'); }
        };

        document.addEventListener('visibilitychange', handlers.visibilitychange);
        window.addEventListener('pagehide', handlers.pagehide);
        window.addEventListener('pageshow', handlers.pageshow);
        watchers[id] = handlers;
    },

    LongLeapAnalyticsUnwatchVisibility: function (id) {
        var watchers = Module.longLeapAnalyticsWatchers;
        var handlers = watchers && watchers[id];
        if (!handlers) {
            return;
        }

        document.removeEventListener('visibilitychange', handlers.visibilitychange);
        window.removeEventListener('pagehide', handlers.pagehide);
        window.removeEventListener('pageshow', handlers.pageshow);
        delete watchers[id];
    }
});
