mergeInto(LibraryManager.library, {
    // Queues a POST that the browser delivers even if the page is closing.
    //
    // The body goes as a plain string, i.e. text/plain: a CORS "simple"
    // request, which sendBeacon requires (it cannot do a preflight). GA4
    // parses the JSON regardless of the content type.
    //
    // sendBeacon refuses payloads over ~64 KB or when its queue is full;
    // fetch with keepalive has the same delivery guarantee and is the
    // fallback. Returns 1 when the browser accepted the request, 0 otherwise.
    LongLeapAnalyticsSendBeacon: function (urlPtr, bodyPtr) {
        var url = UTF8ToString(urlPtr);
        var body = UTF8ToString(bodyPtr);

        try {
            if (navigator.sendBeacon && navigator.sendBeacon(url, body)) {
                return 1;
            }
        } catch (e) {
            // Fall through to fetch.
        }

        try {
            if (typeof fetch === 'function') {
                fetch(url, { method: 'POST', body: body, keepalive: true, mode: 'no-cors' })
                    .catch(function () { /* Fire-and-forget: nothing to report back. */ });
                return 1;
            }
        } catch (e) {
            // Neither path is available.
        }

        return 0;
    },

    // Reports page visibility changes to a GameObject via SendMessage:
    // OnPageHidden when the tab is hidden or closing, OnPageVisible when it
    // comes back. 'pagehide' covers browsers that skip visibilitychange on
    // close (older Safari).
    LongLeapAnalyticsWatchVisibility: function (gameObjectNamePtr) {
        var target = UTF8ToString(gameObjectNamePtr);
        var hidden = false;

        function report(isHidden) {
            if (hidden === isHidden) {
                return;
            }
            hidden = isHidden;
            SendMessage(target, isHidden ? 'OnPageHidden' : 'OnPageVisible');
        }

        document.addEventListener('visibilitychange', function () {
            report(document.visibilityState === 'hidden');
        });
        window.addEventListener('pagehide', function () {
            report(true);
        });
    }
});
