mergeInto(LibraryManager.library, {
    // Открывает окно входа Xsolla ID и ждёт, пока страница обратного вызова
    // пришлёт code и state через postMessage.
    //
    // window.open ОБЯЗАН вызываться внутри обработчика клика: если между
    // жестом игрока и этим вызовом успеет произойти await, браузер сочтёт
    // окно непрошеным и заблокирует. Поэтому адрес приходит сюда готовым, а
    // discovery прогревается заранее, на старте игры.
    OpenXsollaIdPopup: function (urlPtr) {
        var url = UTF8ToString(urlPtr);

        var popup = window.open(
            url,
            'XsollaIdLogin',
            'width=600,height=700,resizable=yes,scrollbars=yes'
        );

        // Заблокировано — сообщаем в C#, чтобы показать подсказку и кнопку
        // «повторить», а не молчать.
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

            // Слушаем только своё окно: чужая страница не должна иметь
            // возможности подсунуть нам код.
            if (event.source !== popup) {
                return;
            }

            if (finished) return;
            finished = true;
            cleanup();

            if (popup && !popup.closed) {
                popup.close();
            }

            // Строкой, а не объектом: SendMessage умеет только строки.
            SendMessage('XsollaIdCallbacks', 'OnAuthCallback', JSON.stringify({
                code: event.data.code || '',
                state: event.data.state || '',
                error: event.data.error || ''
            }));
        }

        window.addEventListener('message', onMessage, false);

        // Игрок может просто закрыть окно, ничего не выбрав — тогда никакого
        // postMessage не будет, и без опроса мы ждали бы до таймаута.
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

    // ---- декодирование картинки профиля силами браузера ----
    //
    // Xsolla ID отдаёт аватар в WEBP (cdn.xsolla.net/.../*.webp), а
    // DownloadHandlerTexture умеет только PNG и JPEG: байты доезжают, а
    // декодер спотыкается и возвращает DataProcessingError. Браузер webp
    // (и avif, и всё будущее) декодирует сам — поэтому картинку забираем
    // здесь, а в Unity отдаём уже сырой RGBA.
    //
    // Состояние держим в глобале на window, а не в $-переменной Emscripten:
    // так нет ни одной тонкости с __deps при линковке, а ошибиться в ней
    // означало бы сломать сборку целиком ради вспомогательной картинки.
    //
    // Протокол намеренно опросный (begin → poll → copy → release): ровно так
    // же весь остальной пакет ждёт сеть — `while (!op.isDone) await
    // Task.Yield()`, единственный безопасный способ под WebGL, где потоков нет.
    // Так не нужен ни SendMessage, ни объект-приёмник на сцене.
    XsollaIdBeginImageDecode: function (urlPtr) {
        var url = UTF8ToString(urlPtr);

        if (!window.__xsollaIdImages) {
            window.__xsollaIdImages = { next: 1, map: {} };
        }

        var store = window.__xsollaIdImages;
        var handle = store.next++;

        // 0 — качаем, 1 — готово, 2 — не доехало (сеть или CORS),
        // 3 — доехало, но не разобралось как картинка.
        // fetched отделяет «не доехало» от «доехало, но не разобралось»:
        // сетевой отказ игроку предлагают повторить, а неподдерживаемый формат
        // повторять бессмысленно — выйдет ровно то же самое.
        var entry = { status: 0, width: 0, height: 0, pixels: null, fetched: false };
        store.map[handle] = entry;

        fetch(url, { mode: 'cors', credentials: 'omit' })
            .then(function (response) {
                if (!response.ok) {
                    // Сервер ответил, но не картинкой: 403/404 у CDN.
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

                // Рисуем перевёрнутой по вертикали: getImageData отдаёт строки
                // сверху вниз, а текстура Unity разложена снизу вверх. Дешевле
                // перевернуть здесь, чем потом гонять построчное копирование.
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

                // Байты уже были у нас — значит споткнулся декодер, а не сеть.
                // Сеть и срез по CORS неотличимы друг от друга и оба дают 2.
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

    // Копирует RGBA прямо в кучу Unity. Возвращает 1 при успехе.
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

        // Пиксели держать незачем: в Unity уже лежит своя копия, а аватар
        // размером с полмегабайта в куче JS — чистая утечка.
        store.map[handle].pixels = null;
        delete store.map[handle];
    }
});
