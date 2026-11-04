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
    }
});
