using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine.Networking;

namespace LongLeap.Backpack
{
    // Что уходит на сервер. Отдельный DTO, а не склейка строк: поле называется
    // "event" — ключевое слово C#, и через [JsonProperty] это единственный
    // способ получить нужное имя, не рискуя ручным экранированием значений
    // (id ачивки и xsollaID приходят извне).
    //
    // Имена полей — контракт сервера (RewardRequest: gameId, xsollaId, event,
    // eventId), поэтому переименовывать их здесь нельзя, даже если в игре эта
    // величина зовётся иначе.
    public sealed class BackPackEventBody
    {
        [JsonProperty("gameId")] public string GameId;
        [JsonProperty("xsollaId")] public string XsollaId;
        [JsonProperty("event")] public string Event;
        [JsonProperty("eventId")] public string EventId;
    }

    // Сырой ответ транспорта: либо код состояния с телом, либо транспортная
    // ошибка (запрос вообще не дошёл). Разделение то же, что в XsollaIdHttp:
    // «сервер сказал 400» и «сети нет» ведут к разным решениям — во втором
    // случае повторять имеет смысл, в первом обычно нет.
    public readonly struct BackPackHttpReply
    {
        public long StatusCode { get; }
        public string Body { get; }
        public string TransportError { get; }

        public bool IsTransportError => TransportError != null;
        public bool IsHttpSuccess => !IsTransportError && StatusCode >= 200 && StatusCode < 300;

        public BackPackHttpReply(long statusCode, string body, string transportError)
        {
            StatusCode = statusCode;
            Body = body;
            TransportError = transportError;
        }

        public static BackPackHttpReply Transport(string error) => new BackPackHttpReply(0, null, error);
        public static BackPackHttpReply Ok(string body) => new BackPackHttpReply(200, body, null);
        public static BackPackHttpReply Status(long code, string body = null) => new BackPackHttpReply(code, body, null);
    }

    // Разобранный ответ BackPack-сервера.
    //
    // Success — единственное, по чему решают «сохранять ачивку или нет».
    // RewardGranted отдельно от Success намеренно: «награда за это событие не
    // предусмотрена» — это УДАЧНЫЙ ответ (success: true, rewardGranted:
    // false), и ачивку по нему сохранять надо.
    public readonly struct BackPackReply
    {
        public bool Success { get; }
        public bool RewardGranted { get; }
        public string RewardId { get; }

        // Код ошибки от сервера ("invalid_client") либо описание сбоя, до
        // которого сервер не дошёл. Для лога, не для игрока.
        public string Error { get; }

        private BackPackReply(bool success, bool rewardGranted, string rewardId, string error)
        {
            Success = success;
            RewardGranted = rewardGranted;
            RewardId = rewardId;
            Error = error;
        }

        public static BackPackReply Ok(bool rewardGranted, string rewardId) =>
            new BackPackReply(true, rewardGranted, rewardId, null);

        public static BackPackReply Failed(string error) =>
            new BackPackReply(false, false, null, error ?? "unknown_error");

        public override string ToString() => Success
            ? $"BackPackReply(ok, rewardGranted={RewardGranted}, rewardId={RewardId ?? "none"})"
            : $"BackPackReply(failed, error={Error})";
    }

    // Отправка игровых событий на BackPack-сервер.
    //
    // Своих умолчаний у сервиса нет: адрес, путь и gameId приходят снаружи, из
    // BackPackConfigSO. Он не должен знать, на какой машине стоит сервер, —
    // иначе смена хоста была бы правкой кода, а не ассета.
    //
    // Транспорт свой, а не XsollaIdHttp: та сборка (LongLeap.XsollaId.Web)
    // живёт под defineConstraints XSOLLA_ID, и опора на неё выключала бы
    // отправку событий вместе с выключенным входом. Форма скопирована оттуда
    // же: UnityWebRequest, ожидание через while (!op.isDone) await
    // Task.Yield() (под WebGL потоков нет, это единственный рабочий вариант),
    // timeout = 10.
    //
    // Наружу исключения НЕ летят никогда: любой сбой приезжает как
    // BackPackReply.Failed. Отправку зовут из фоновой задачи, которую никто не
    // дожидается, — брошенное там исключение утонуло бы молча.
    public sealed class BackPackService
    {
        private const int TimeoutSeconds = 10;

        // Ручка рюкзака. Путь фиксированный, в отличие от eventsPath: настраивать
        // его никто не просил, а зашитая здесь строка честнее пустого поля в
        // ассете, о котором все забудут.
        private const string BackpackItemsPath = "/backpack/items";

        private readonly string _baseUrl;
        private readonly string _url;
        private readonly string _gameId;
        private readonly Func<string> _xsollaIdProvider;
        private readonly Func<string, string, Task<BackPackHttpReply>> _handler;
        private readonly Func<string, Task<BackPackHttpReply>> _deleteHandler;

        // xsollaIdProvider — делегат, а не строка: xsollaID появляется не в
        // момент создания сервиса, а когда игрок войдёт (или когда тихо
        // восстановится сессия), и меняется при выходе-входе.
        //
        // baseUrl, gameId и eventsPath — без значений по умолчанию: все три
        // берутся из BackPackConfigSO, и подставленное здесь умолчание молча
        // расходилось бы с ассетом.
        //
        // handler подменяют тесты: он получает (url, тело) и отдаёт сырой
        // ответ, поэтому проверять разбор json-а можно без сервера.
        // deleteHandler — то же самое для DELETE, где тела запроса нет.
        public BackPackService(
            Func<string> xsollaIdProvider,
            string baseUrl,
            string gameId,
            string eventsPath,
            Func<string, string, Task<BackPackHttpReply>> handler = null,
            Func<string, Task<BackPackHttpReply>> deleteHandler = null)
        {
            _baseUrl = string.IsNullOrEmpty(baseUrl) ? null : baseUrl.TrimEnd('/');

            // Пустой путь не заменяем умолчанием (его подставляет конфиг, ему
            // виднее), а считаем сервис ненастроенным: запрос по одному
            // baseUrl ушёл бы в корень сервера и до нужной ручки не дошёл.
            _url = _baseUrl == null || string.IsNullOrEmpty(eventsPath)
                ? null
                : _baseUrl + eventsPath;
            _gameId = gameId;
            _xsollaIdProvider = xsollaIdProvider;
            _handler = handler ?? SendAsync;
            _deleteHandler = deleteHandler ?? SendDeleteAsync;
        }

        // Настроен ли сервис вообще. Проверяется до отправки, чтобы пустой
        // конфиг не превращался в запрос по битому адресу.
        public bool IsAvailable => !string.IsNullOrEmpty(_url) && !string.IsNullOrEmpty(_gameId);

        // Есть ли, от чьего имени отправлять. Отдельно от IsAvailable: гость —
        // не поломка конфига, а обычное состояние до входа.
        public bool HasXsollaId => !string.IsNullOrEmpty(XsollaId);

        public string XsollaId => _xsollaIdProvider?.Invoke();

        public string Url => _url;

        // Отправить событие. eventName — имя события на стороне сервера; для
        // ачивок это их id ("tutorial_completed").
        //
        // eventId по умолчанию считается детерминированно из (gameId,
        // xsollaId, eventName) — см. StableEventId.
        public async Task<BackPackReply> SendEventAsync(string eventName, string eventId = null)
        {
            if (string.IsNullOrEmpty(eventName))
                throw new ArgumentException("BackPackService: имя события не может быть пустым.", nameof(eventName));

            if (!IsAvailable)
                return BackPackReply.Failed("not_configured");

            string xsollaId = XsollaId;

            if (string.IsNullOrEmpty(xsollaId))
                return BackPackReply.Failed("no_xsolla_id");

            string body = JsonConvert.SerializeObject(new BackPackEventBody
            {
                GameId = _gameId,
                XsollaId = xsollaId,
                Event = eventName,
                EventId = string.IsNullOrEmpty(eventId) ? StableEventId(_gameId, xsollaId, eventName) : eventId
            });

            BackPackHttpReply raw;

            try
            {
                raw = await _handler(_url, body);
            }
            catch (Exception e)
            {
                return BackPackReply.Failed($"transport: {e.Message}");
            }

            return Parse(raw);
        }

        // Дальше — две ОТЛАДОЧНЫЕ ручки удаления. Обе живут на сервере только в
        // среде Development (в остальных отвечают 404), обе требуют xsollaID —
        // удаляется всегда прогресс конкретного игрока, никакого «удалить у
        // всех» здесь нет и быть не может.
        //
        // Ответ отдаём СЫРЫМ (BackPackHttpReply), а не разобранным: тело у них
        // не в форме BackPackEventReplyBody — это отчёт со списком строк, и
        // нужен он ровно для того, чтобы лечь в лог целиком. Заводить под него
        // DTO значило бы тащить форму отладочного отчёта в игровой код.

        // DELETE /backpack/items?xsollaId=…[&sku=…][&itemId=…]
        //
        // Убирает предметы из рюкзака игрока. Без sku и itemId — весь рюкзак.
        public Task<BackPackHttpReply> DeleteBackpackItemsAsync(string sku = null, string itemId = null)
        {
            if (string.IsNullOrEmpty(_baseUrl))
                return Task.FromResult(BackPackHttpReply.Transport("not_configured"));

            string xsollaId = XsollaId;

            if (string.IsNullOrEmpty(xsollaId))
                return Task.FromResult(BackPackHttpReply.Transport("no_xsolla_id"));

            var url = new StringBuilder(_baseUrl)
                .Append(BackpackItemsPath)
                .Append("?xsollaId=").Append(UnityWebRequest.EscapeURL(xsollaId));

            Append(url, "sku", sku);
            Append(url, "itemId", itemId);

            return SendDeleteSafeAsync(url.ToString());
        }

        // DELETE /events?xsollaId=&gameId=…[&eventId=…][&sku=…]&backpack=&force=
        //
        // Убирает записи о выданных наградах — то, чем сервер защищается от
        // повторной выдачи. Без них те же события выдадут награду заново, и
        // именно это обычно и нужно при проверке.
        //
        // gameId подставляется сам и всегда: чужие игры того же игрока к нашей
        // проверке отношения не имеют, а снести их одной кнопкой — слишком.
        //
        // backpack = true — сервер попутно отзовёт и сам предмет; force = false
        // — записи, чей предмет отозвать не удалось, остаются на месте (иначе
        // повторная выдача создала бы дубликат).
        public Task<BackPackHttpReply> DeleteClaimsAsync(
            string eventId = null,
            string sku = null,
            bool backpack = true,
            bool force = false)
        {
            if (!IsAvailable)
                return Task.FromResult(BackPackHttpReply.Transport("not_configured"));

            string xsollaId = XsollaId;

            if (string.IsNullOrEmpty(xsollaId))
                return Task.FromResult(BackPackHttpReply.Transport("no_xsolla_id"));

            var url = new StringBuilder(_url)
                .Append("?xsollaId=").Append(UnityWebRequest.EscapeURL(xsollaId));

            Append(url, "gameId", _gameId);
            Append(url, "eventId", eventId);
            Append(url, "sku", sku);

            url.Append("&backpack=").Append(backpack ? "true" : "false")
               .Append("&force=").Append(force ? "true" : "false");

            return SendDeleteSafeAsync(url.ToString());
        }

        private static void Append(StringBuilder url, string name, string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            url.Append('&').Append(name).Append('=').Append(UnityWebRequest.EscapeURL(value));
        }

        // Та же гарантия, что у SendEventAsync: наружу исключения не летят,
        // любой сбой приезжает ответом. Зовут это из обработчика кнопки —
        // async void, где брошенное исключение утонуло бы молча.
        private async Task<BackPackHttpReply> SendDeleteSafeAsync(string url)
        {
            try
            {
                return await _deleteHandler(url);
            }
            catch (Exception e)
            {
                return BackPackHttpReply.Transport($"DELETE {url} — {e.Message}");
            }
        }

        private static BackPackReply Parse(BackPackHttpReply raw)
        {
            if (raw.IsTransportError)
                return BackPackReply.Failed($"transport: {raw.TransportError}");

            if (string.IsNullOrWhiteSpace(raw.Body))
                return BackPackReply.Failed($"http_{raw.StatusCode}_empty_body");

            BackPackEventReplyBody parsed;

            try
            {
                parsed = JsonConvert.DeserializeObject<BackPackEventReplyBody>(raw.Body);
            }
            catch (Exception e)
            {
                return BackPackReply.Failed($"bad_json: {e.Message}");
            }

            if (parsed == null)
                return BackPackReply.Failed($"http_{raw.StatusCode}_bad_json");

            // Тело разбираем и при неуспешном коде состояния: причину отказа
            // ("invalid_client") сервер кладёт именно туда. А удачей считаем
            // только совпадение обоих признаков — success: true с кодом 500
            // это рассинхрон, и сохранять по нему нельзя.
            if (!parsed.Success)
                return BackPackReply.Failed(parsed.Error ?? $"http_{raw.StatusCode}_rejected");

            if (!raw.IsHttpSuccess)
                return BackPackReply.Failed($"http_{raw.StatusCode}_with_success_body");

            return BackPackReply.Ok(parsed.RewardGranted, parsed.RewardId);
        }

        // eventId — ключ идемпотентности, поэтому он ДЕТЕРМИНИРОВАННЫЙ, а не
        // Guid.NewGuid() на каждую попытку.
        //
        // Причина в правиле «сохраняем только при удачном ответе»: пока ответа
        // нет, ачивка не записана, и в следующей сессии событие уйдёт
        // повторно. Со случайным id сервер увидел бы два разных события и мог
        // бы выдать награду дважды; с этим — одно и то же событие, и
        // дедупликация на сервере срабатывает.
        //
        // Хеш не криптографический (FNV-1a с двух засевов), потому что
        // работоспособность System.Security.Cryptography под IL2CPP/WebGL в
        // этом проекте ещё не подтверждена (см. предупреждение в Pkce), а
        // ключу дедупликации стойкость и не нужна — нужна воспроизводимость и
        // отсутствие случайных совпадений.
        public static string StableEventId(string gameId, string xsollaId, string eventName)
        {
            byte[] payload = Encoding.UTF8.GetBytes($"{gameId}|{xsollaId}|{eventName}");
            var bytes = new byte[16];

            BitConverter.GetBytes(Fnv1a64(payload, 0xcbf29ce484222325UL)).CopyTo(bytes, 0);
            BitConverter.GetBytes(Fnv1a64(payload, 0x9e3779b97f4a7c15UL)).CopyTo(bytes, 8);

            // Приводим к виду UUID версии 3 (name-based): сервер ждёт uuid, а
            // не произвольные 32 hex-символа.
            bytes[6] = (byte)((bytes[6] & 0x0F) | 0x30);
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

            return new Guid(bytes).ToString();
        }

        private static ulong Fnv1a64(byte[] data, ulong seed)
        {
            ulong hash = seed;

            foreach (byte b in data)
            {
                hash ^= b;
                hash *= 0x100000001b3UL;
            }

            return hash;
        }

        private static async Task<BackPackHttpReply> SendAsync(string url, string body)
        {
            using var request = new UnityWebRequest(url, "POST");
            request.timeout = TimeoutSeconds;
            request.downloadHandler = new DownloadHandlerBuffer();
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json");

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();

            while (!operation.isDone)
                await Task.Yield();

            if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.DataProcessingError)
            {
                // Сети не хватило — кода состояния нет вовсе.
                return BackPackHttpReply.Transport($"POST {url} — {request.error}");
            }

            return new BackPackHttpReply(request.responseCode, request.downloadHandler?.text, null);
        }

        // UnityWebRequest.Delete приходит БЕЗ downloadHandler — без этой строки
        // тело ответа читать нечем, и отчёт сервера об удалении пропал бы. В
        // остальном форма та же, что у SendAsync.
        private static async Task<BackPackHttpReply> SendDeleteAsync(string url)
        {
            using var request = UnityWebRequest.Delete(url);
            request.timeout = TimeoutSeconds;
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Accept", "application/json");

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();

            while (!operation.isDone)
                await Task.Yield();

            if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.DataProcessingError)
            {
                return BackPackHttpReply.Transport($"DELETE {url} — {request.error}");
            }

            return new BackPackHttpReply(request.responseCode, request.downloadHandler?.text, null);
        }

        // Тело ответа. Приватный DTO: наружу отдаём BackPackReply, чтобы форма
        // серверного json-а не расползалась по игре.
        private sealed class BackPackEventReplyBody
        {
            [JsonProperty("success")] public bool Success;
            [JsonProperty("rewardGranted")] public bool RewardGranted;
            [JsonProperty("rewardId")] public string RewardId;
            [JsonProperty("error")] public string Error;
        }
    }
}
