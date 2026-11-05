var GamePixXsollaIdLib = {
    // Bridge to GamePix.user from SDK v3, used by XsollaIdAuthJS (GAMEPIX_JS):
    // the portal signs in through Xsolla ID itself and hands the game a ready
    // token. This file only calls the SDK and forwards the answer to C# via
    // SendMessage — all logic lives in XsollaIdAuthJS.cs.
    //
    // Without GAMEPIX_JS nothing in C# imports these functions, and Emscripten
    // links only referenced library functions, so the file adds nothing to the
    // build.
    //
    // The answer is sent as a JSON string: SendMessage only carries strings.
    // The token in it is the player's credential, so never console.log it.

    // Flattens the SDK answer into an object whose fields are always present:
    // C# should not have to guess whether it got null, undefined or nothing.
    $GpxXsollaIdPack: function (requestId, auth) {
        auth = auth || {};
        var user = auth.user || {};

        return JSON.stringify({
            requestId: requestId,
            success: !!auth.success,
            code: typeof auth.code === 'number' ? auth.code : 0,
            message: auth.message ? String(auth.message) : '',
            id: user.id != null ? String(user.id) : '',
            name: user.name || '',
            nickname: user.nickname || '',
            username: user.username || '',
            avatar: user.avatar || '',
            guest: !!user.guest,
            token: auth.token || '',
            expiresAt: typeof auth.expiresAt === 'number' ? auth.expiresAt : 0
        });
    },

    // 1 if the SDK is loaded and has the user API. Older SDK builds lack it —
    // then sign-in is hidden.
    GpxXsollaIdIsSupported: function () {
        var gpx = window.GamePix;

        return gpx && gpx.user && typeof gpx.user.getUser === 'function' ? 1 : 0;
    },

    // login == 0 — GamePix.user.getUser(): never shows a window; a guest gets
    // 401.
    // login == 1 — GamePix.user.requestLogin(): the portal login window. Call
    // it ONLY from a click handler: the window opens synchronously inside the
    // call while the player's gesture is still alive, so there is no await
    // before the call here.
    GpxXsollaIdCall__deps: ['$GpxXsollaIdPack'],
    GpxXsollaIdCall: function (requestId, login) {
        function reply(auth) {
            SendMessage('GamePixXsollaIdCallbacks', 'OnAuthResult', GpxXsollaIdPack(requestId, auth));
        }

        function fail(e) {
            reply({ success: false, code: 0, message: String((e && e.message) || e) });
        }

        var method = login ? 'requestLogin' : 'getUser';
        var gpx = window.GamePix;

        if (!gpx || !gpx.user || typeof gpx.user[method] !== 'function') {
            reply({ success: false, code: 404, message: 'GamePix user API not found' });
            return;
        }

        var promise;

        try {
            promise = gpx.user[method]();
        } catch (e) {
            fail(e);
            return;
        }

        Promise.resolve(promise).then(reply, fail);
    },

    // authChanged: the SDK renews the token itself, and the player may sign in
    // or out on the GamePix site while the game is open. Subscribe once per
    // page — reloading the scene after death must not pile up handlers.
    GpxXsollaIdSubscribe__deps: ['$GpxXsollaIdPack'],
    // Returns 1 — subscribed (now or by a previous scene), 0 — the SDK has no
    // GamePix.events.on and the game will never hear about authChanged. C#
    // logs this.
    GpxXsollaIdSubscribe: function () {
        if (window.__gpxXsollaIdSubscribed) return 1;

        var gpx = window.GamePix;
        if (!gpx || !gpx.events || typeof gpx.events.on !== 'function') return 0;

        window.__gpxXsollaIdSubscribed = true;

        gpx.events.on('authChanged', function (auth) {
            SendMessage('GamePixXsollaIdCallbacks', 'OnAuthChanged', GpxXsollaIdPack(0, auth));
        });

        return 1;
    }
};

mergeInto(LibraryManager.library, GamePixXsollaIdLib);
