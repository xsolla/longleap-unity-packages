mergeInto(LibraryManager.library, {
    // Is the primary pointer a finger without hover? The same media query the
    // orientation overlay in index.html uses (WebGlPagePostprocessor): the page and
    // DeviceInfo must agree on whether the device is mobile.
    // 1 — yes, 0 — no (mouse, touchpad), -1 — the browser has no matchMedia.
    LongLeapToolsPrimaryPointerCoarse: function () {
        if (typeof window === 'undefined' || !window.matchMedia) {
            return -1;
        }

        try {
            return window.matchMedia('(hover: none) and (pointer: coarse)').matches ? 1 : 0;
        } catch (e) {
            return -1;
        }
    },

    // Page visibility for PageVisibility.cs: calls callback(1) when the tab is
    // hidden or the page is being unloaded, callback(0) when it is shown again
    // (including a back/forward cache restore). Duplicate states are filtered.
    // Registered once per page; a second call only returns the state.
    // Returns the current state: 1 hidden, 0 visible.
    LongLeapToolsWatchVisibility: function (callback) {
        var state = Module.longLeapToolsVisibility;
        if (state) {
            return state.hidden ? 1 : 0;
        }

        state = Module.longLeapToolsVisibility = { hidden: document.visibilityState === 'hidden' };

        function report(isHidden) {
            if (state.hidden === isHidden) {
                return;
            }
            state.hidden = isHidden;
            {{{ makeDynCall('vi', 'callback') }}}(isHidden ? 1 : 0);
        }

        document.addEventListener('visibilitychange', function () { report(document.visibilityState === 'hidden'); });
        window.addEventListener('pagehide', function () { report(true); });
        window.addEventListener('pageshow', function () { report(document.visibilityState === 'hidden'); });

        return state.hidden ? 1 : 0;
    }
});
