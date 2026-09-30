var GamePixXsollaIdLib = {
    // Мост к GamePix.user из SDK v3: площадка сама входит через Xsolla ID и
    // отдаёт игре готовый токен. Здесь только вызов SDK и пересылка ответа в
    // C# через SendMessage — вся логика в XsollaIdAuthJS.cs.
    //
    // Ответ уходит строкой JSON: SendMessage умеет только строки. Токен в нём —
    // учётные данные игрока, поэтому ни одного console.log с ответом.

    // Сводит ответ SDK к плоскому объекту с полями, которые всегда есть:
    // C# не должен гадать, пришёл ли null, undefined или не пришло ничего.
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

    // 1, если SDK загружен и в нём есть пользовательский API. У старых сборок
    // SDK его нет — тогда вход прячется.
    GpxXsollaIdIsSupported: function () {
        var gpx = window.GamePix;

        return gpx && gpx.user && typeof gpx.user.getUser === 'function' ? 1 : 0;
    },

    // login == 0 — GamePix.user.getUser(): окна не показывает никогда, гость
    // получает 401.
    // login == 1 — GamePix.user.requestLogin(): окно входа площадки. Зовётся
    // ТОЛЬКО из обработчика нажатия: окно открывается синхронно внутри вызова,
    // пока жест игрока ещё жив, поэтому до вызова здесь нет ни одного await.
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

    // authChanged: SDK сам обновляет токен, а игрок может войти или выйти на
    // сайте GamePix, пока игра открыта. Подписываемся один раз на страницу —
    // перезагрузка сцены после смерти не должна плодить обработчики.
    GpxXsollaIdSubscribe__deps: ['$GpxXsollaIdPack'],
    // Возвращает 1 — подписка есть (новая или с прошлой сцены), 0 — у SDK нет
    // GamePix.events.on, и об authChanged игра не узнает. C# это логирует.
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
