mergeInto(LibraryManager.library, {
    // Opens the Xsolla ID sign-in window and waits for the callback page to
    // post code and state back via postMessage.
    //
    // window.open MUST be called inside the click handler: if an await happens
    // between the player's gesture and this call, the browser treats the window
    // as unsolicited and blocks it. That is why the URL arrives here ready-made
    // and discovery is warmed up in advance, at game startup.
    OpenXsollaIdPopup: function (urlPtr) {
        var url = UTF8ToString(urlPtr);

        var popup = window.open(
            url,
            'XsollaIdLogin',
            'width=600,height=700,resizable=yes,scrollbars=yes'
        );

        // Blocked — tell C# so it can show a hint and a "retry" button instead
        // of staying silent.
        if (!popup || popup.closed || typeof popup.closed === 'undefined') {
            SendMessage('XsollaIdCallbacks', 'OnPopupBlocked');
            return;
        }

        var finished = false;

        function cleanup() {
            if (closePoll) {
                clearInterval(closePoll);
                closePoll = null;
            }
            window.removeEventListener('message', onMessage, false);
        }

        function onMessage(event) {
            if (!event.data || event.data.type !== 'xsolla-id-auth') {
                return;
            }

            // Listen only to our own window: another page must not be able to
            // slip us a code.
            if (event.source !== popup) {
                return;
            }

            if (finished) return;
            finished = true;
            cleanup();

            if (popup && !popup.closed) {
                popup.close();
            }

            // As a string, not an object: SendMessage only accepts strings.
            SendMessage('XsollaIdCallbacks', 'OnAuthCallback', JSON.stringify({
                code: event.data.code || '',
                state: event.data.state || '',
                error: event.data.error || ''
            }));
        }

        window.addEventListener('message', onMessage, false);

        // The player may simply close the window without choosing anything —
        // then no postMessage arrives, and without polling we would wait until
        // the timeout.
        var closePoll = setInterval(function () {
            if (!popup || !popup.closed) {
                return;
            }

            if (finished) {
                cleanup();
                return;
            }

            finished = true;
            cleanup();

            SendMessage('XsollaIdCallbacks', 'OnAuthCancelled');
        }, 500);
    },

    // ---- profile picture decoding by the browser ----
    //
    // Xsolla ID serves avatars as WEBP (cdn.xsolla.net/.../*.webp), while
    // DownloadHandlerTexture only handles PNG and JPEG: the bytes arrive, but
    // the decoder fails with DataProcessingError. The browser decodes webp (and
    // avif, and whatever comes next) natively — so the image is fetched here
    // and Unity receives raw RGBA.
    //
    // State is kept in a global on window rather than an Emscripten $-variable:
    // that avoids any __deps subtleties at link time, where a mistake would
    // break the whole build for the sake of a secondary image.
    //
    // The protocol is deliberately polling-based (begin → poll → copy →
    // release): the rest of the package waits for the network the same way —
    // `while (!op.isDone) await Task.Yield()`, the only safe option under
    // WebGL, which has no threads. No SendMessage and no receiver object in the
    // scene are needed.
    XsollaIdBeginImageDecode: function (urlPtr) {
        var url = UTF8ToString(urlPtr);

        if (!window.__xsollaIdImages) {
            window.__xsollaIdImages = { next: 1, map: {} };
        }

        var store = window.__xsollaIdImages;
        var handle = store.next++;

        // 0 — downloading, 1 — ready, 2 — did not arrive (network or CORS),
        // 3 — arrived but could not be decoded as an image.
        // fetched separates "did not arrive" from "arrived but not decoded": a
        // network failure invites a retry, while retrying an unsupported format
        // is pointless — the result would be the same.
        var entry = { status: 0, width: 0, height: 0, pixels: null, fetched: false };
        store.map[handle] = entry;

        fetch(url, { mode: 'cors', credentials: 'omit' })
            .then(function (response) {
                if (!response.ok) {
                    // The server answered, but not with an image: 403/404 from the CDN.
                    entry.status = 3;
                    return null;
                }

                entry.fetched = true;

                return response.blob();
            })
            .then(function (blob) {
                if (!blob) return null;

                return createImageBitmap(blob);
            })
            .then(function (bitmap) {
                if (!bitmap) return;

                var canvas = document.createElement('canvas');
                canvas.width = bitmap.width;
                canvas.height = bitmap.height;

                var ctx = canvas.getContext('2d');

                // Draw flipped vertically: getImageData returns rows top to
                // bottom, while a Unity texture is laid out bottom to top.
                // Flipping here is cheaper than copying row by row later.
                ctx.translate(0, canvas.height);
                ctx.scale(1, -1);
                ctx.drawImage(bitmap, 0, 0);

                if (bitmap.close) bitmap.close();

                var data = ctx.getImageData(0, 0, canvas.width, canvas.height).data;

                entry.width = canvas.width;
                entry.height = canvas.height;
                entry.pixels = new Uint8Array(data.buffer.slice(0));
                entry.status = 1;
            })
            .catch(function (e) {
                if (entry.status !== 0) return;

                // We already had the bytes — so the decoder failed, not the
                // network. Network errors and CORS rejections are
                // indistinguishable and both yield 2.
                entry.status = entry.fetched ? 3 : 2;
            });

        return handle;
    },

    XsollaIdPollImageDecode: function (handle) {
        var store = window.__xsollaIdImages;
        if (!store || !store.map[handle]) return 2;

        return store.map[handle].status;
    },

    XsollaIdImageWidth: function (handle) {
        var store = window.__xsollaIdImages;
        if (!store || !store.map[handle]) return 0;

        return store.map[handle].width;
    },

    XsollaIdImageHeight: function (handle) {
        var store = window.__xsollaIdImages;
        if (!store || !store.map[handle]) return 0;

        return store.map[handle].height;
    },

    // Copies RGBA straight into the Unity heap. Returns 1 on success.
    XsollaIdCopyImagePixels: function (handle, dstPtr, byteLength) {
        var store = window.__xsollaIdImages;
        if (!store || !store.map[handle]) return 0;

        var entry = store.map[handle];
        if (entry.status !== 1 || !entry.pixels) return 0;
        if (entry.pixels.length !== byteLength) return 0;

        HEAPU8.set(entry.pixels, dstPtr);

        return 1;
    },

    XsollaIdReleaseImage: function (handle) {
        var store = window.__xsollaIdImages;
        if (!store || !store.map[handle]) return;

        // No reason to keep the pixels: Unity already has its own copy, and a
        // half-megabyte avatar in the JS heap is a plain leak.
        store.map[handle].pixels = null;
        delete store.map[handle];
    }
});
