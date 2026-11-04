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

    // Reports page visibility to a GameObject via SendMessage: OnPageHidden
    // when the tab is hidden or the page is being unloaded, OnPageVisible when
    // it is shown again (including a back/forward cache restore). Duplicate
    // states are filtered. Listeners are kept per GameObject so they can be
    // removed again by LongLeapAnalyticsUnwatchVisibility.
    LongLeapAnalyticsWatchVisibility: function (gameObjectNamePtr) {
        var target = UTF8ToString(gameObjectNamePtr);
        var watchers = Module.longLeapAnalyticsWatchers || (Module.longLeapAnalyticsWatchers = {});
        if (watchers[target]) {
            return;
        }

        var state = { hidden: document.visibilityState === 'hidden' };

        function report(isHidden) {
            if (state.hidden === isHidden) {
                return;
            }
            state.hidden = isHidden;
            SendMessage(target, isHidden ? 'OnPageHidden' : 'OnPageVisible');
        }

        var handlers = {
            visibilitychange: function () { report(document.visibilityState === 'hidden'); },
            pagehide: function () { report(true); },
            pageshow: function () { report(document.visibilityState === 'hidden'); }
        };

        document.addEventListener('visibilitychange', handlers.visibilitychange);
        window.addEventListener('pagehide', handlers.pagehide);
        window.addEventListener('pageshow', handlers.pageshow);
        watchers[target] = handlers;
    },

    LongLeapAnalyticsUnwatchVisibility: function (gameObjectNamePtr) {
        var target = UTF8ToString(gameObjectNamePtr);
        var watchers = Module.longLeapAnalyticsWatchers;
        var handlers = watchers && watchers[target];
        if (!handlers) {
            return;
        }

        document.removeEventListener('visibilitychange', handlers.visibilitychange);
        window.removeEventListener('pagehide', handlers.pagehide);
        window.removeEventListener('pageshow', handlers.pageshow);
        delete watchers[target];
    }
});
