using System;
using System.Collections.Generic;
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

    // Ответ на «есть ли связь с BackPack-сервером» (см. CheckConnectionAsync).
    // Error — для лога: "not_configured", "transport: …", "http_502".
    public readonly struct BackPackConnectionResult
    {
        public bool IsConnected { get; }
        public string Error { get; }

        private BackPackConnectionResult(bool isConnected, string error)
        {
            IsConnected = isConnected;
            Error = error;
        }

        public static BackPackConnectionResult Connected() => new BackPackConnectionResult(true, null);

        public static BackPackConnectionResult Failed(string error) =>
            new BackPackConnectionResult(false, error ?? "unknown_error");

        public override string ToString() => IsConnected
            ? "BackPackConnection(ok)"
            : $"BackPackConnection(failed, error={Error})";
    }

    // Итог удаления ОДНОГО предмета рюкзака (см. DeleteItemAsync).
    //
    // IsDeleted — только если сервер отчитался, что удалил именно этот
    // экземпляр СЕЙЧАС (outcome "item_deleted"). Код ответа ничего не решает:
    // сервер отвечает 200 и тогда, когда Backpack не удалил предмет
    // ("backpack_failed"), и когда предмета уже не было ("item_already_gone").
    // По IsDeleted игра выдаёт товар — значит, ошибиться в сторону «удалён»
    // нельзя ни при каком ответе.
    public readonly struct BackPackDeleteResult
    {
        public const string OutcomeDeleted = "item_deleted";
        public const string OutcomeAlreadyGone = "item_already_gone";

        public bool IsDeleted { get; }

        // Что сервер сказал про этот предмет ("item_already_gone",
        // "backpack_failed") или null, если до отчёта не дошло.
        public string Outcome { get; }

        // Для лога: причина, если не удалён.
        public string Error { get; }

        private BackPackDeleteResult(bool isDeleted, string outcome, string error)
        {
            IsDeleted = isDeleted;
            Outcome = outcome;
            Error = error;
        }

        public static BackPackDeleteResult Deleted() => new BackPackDeleteResult(true, OutcomeDeleted, null);

        public static BackPackDeleteResult Failed(string error, string outcome = null) =>
            new BackPackDeleteResult(false, outcome, error ?? "unknown_error");

        public override string ToString() => IsDeleted
            ? "BackPackDelete(deleted)"
            : $"BackPackDelete(not deleted, outcome={Outcome ?? "none"}, error={Error})";
    }

    // Один предмет рюкзака игрока — элемент items из ответа
    // GET /rewards?xsollaId=…&gameId=….
    //
    // Имена полей — контракт сервера, как и у BackPackEventBody. Поля, а не
    // свойства — по той же причине, что у остальных DTO здесь: так их
    // заполняет Newtonsoft без оглядки на стриппинг сеттеров под IL2CPP.
    //
    // Sku — тип предмета, Id — конкретный экземпляр в рюкзаке (по нему предмет
    // и удаляют: удаление по sku сносит ВСЕ предметы этого типа).
    //
    // Rewards — ключи наград из reward_items.json сервера по SKU: что игра
    // выдаёт за этот предмет. Что значит каждый ключ, знает игра, а не сервер.
    public sealed class BackPackRewardItem
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("sku")] public string Sku;
        [JsonProperty("name")] public string Name;
        [JsonProperty("description")] public string Description;
        [JsonProperty("iconUrl")] public string IconUrl;
        [JsonProperty("createdAt")] public string CreatedAt;
        [JsonProperty("rewards")] public string[] Rewards;
    }

    // Разобранный ответ на запрос рюкзака.
    //
    // Items никогда не null: при неудаче — пустой список, чтобы экран мог
    // перерисоваться по нему без проверок. Error — для лога, не для игрока:
    // "no_xsolla_id", "not_configured", "http_400: invalid_xsolla_id"…
    public readonly struct BackPackRewardsReply
    {
        private static readonly BackPackRewardItem[] NoItems = Array.Empty<BackPackRewardItem>();

        public bool Success { get; }
        public IReadOnlyList<BackPackRewardItem> Items { get; }

        // 0 — до сервера не дошли (нет сети, не настроено, нет xsollaID).
        public long StatusCode { get; }
        public string Error { get; }

        private BackPackRewardsReply(bool success, IReadOnlyList<BackPackRewardItem> items, long statusCode, string error)
        {
            Success = success;
            Items = items ?? NoItems;
            StatusCode = statusCode;
            Error = error;
        }

        public static BackPackRewardsReply Ok(IReadOnlyList<BackPackRewardItem> items) =>
            new BackPackRewardsReply(true, items, 200, null);

        public static BackPackRewardsReply Failed(string error, long statusCode = 0) =>
            new BackPackRewardsReply(false, null, statusCode, error ?? "unknown_error");

        public override string ToString() => Success
            ? $"BackPackRewardsReply(ok, items={Items.Count})"
            : $"BackPackRewardsReply(failed, status={StatusCode}, error={Error})";
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

        // Проверку связи ждёт игрок, нажавший «Start», — дольше держать его нельзя.
        // Не уложился сервер — считаем, что связи нет, и игра стартует без него.
        private const int ConnectionCheckTimeoutSeconds = 5;

        private readonly string _baseUrl;
        private readonly string _url;
        private readonly string _rewardsUrl;
        private readonly string _itemsUrl;
        private readonly string _gameId;
        private readonly Func<string> _xsollaIdProvider;
        private readonly Func<string, string, Task<BackPackHttpReply>> _handler;
        private readonly Func<string, Task<BackPackHttpReply>> _deleteHandler;
        private readonly Func<string, Task<BackPackHttpReply>> _getHandler;
        private readonly Func<string, Task<BackPackHttpReply>> _connectionCheckHandler;

        // xsollaIdProvider — делегат, а не строка: xsollaID появляется не в
        // момент создания сервиса, а когда игрок войдёт (или когда тихо
        // восстановится сессия), и меняется при выходе-входе.
        //
        // baseUrl, gameId и пути — без значений по умолчанию: всё берётся из
        // BackPackConfigSO, и подставленное здесь умолчание молча расходилось
        // бы с ассетом.
        //
        // handler подменяют тесты: он получает (url, тело) и отдаёт сырой
        // ответ, поэтому проверять разбор json-а можно без сервера.
        // deleteHandler и getHandler — то же самое для DELETE и GET, где тела
        // запроса нет.
        public BackPackService(
            Func<string> xsollaIdProvider,
            string baseUrl,
            string gameId,
            string eventsPath,
            string rewardsPath = null,
            string itemsPath = null,
            Func<string, string, Task<BackPackHttpReply>> handler = null,
            Func<string, Task<BackPackHttpReply>> deleteHandler = null,
            Func<string, Task<BackPackHttpReply>> getHandler = null)
        {
            _baseUrl = string.IsNullOrEmpty(baseUrl) ? null : baseUrl.TrimEnd('/');

            // Пустой путь не заменяем умолчанием (его подставляет конфиг, ему
            // виднее), а считаем ручку ненастроенной: запрос по одному
            // baseUrl ушёл бы в корень сервера и до нужной ручки не дошёл.
            _url = Combine(_baseUrl, eventsPath);
            _rewardsUrl = Combine(_baseUrl, rewardsPath);
            _itemsUrl = Combine(_baseUrl, itemsPath);
            _gameId = gameId;
            _xsollaIdProvider = xsollaIdProvider;
            _handler = handler ?? SendAsync;
            _deleteHandler = deleteHandler ?? SendDeleteAsync;
            _getHandler = getHandler ?? (url => SendGetAsync(url, TimeoutSeconds));

            // Подменённый тестом GET отвечает и за проверку связи: отдельная
            // подмена ради одного таймаута тестам ни к чему.
            _connectionCheckHandler = getHandler ?? (url => SendGetAsync(url, ConnectionCheckTimeoutSeconds));
        }

        // Настроен ли сервис вообще. Проверяется до отправки, чтобы пустой
        // конфиг не превращался в запрос по битому адресу.
        public bool IsAvailable => !string.IsNullOrEmpty(_url) && !string.IsNullOrEmpty(_gameId);

        // Можно ли спросить рюкзак: адрес, путь ручки и gameId на месте.
        public bool IsRewardsAvailable => !string.IsNullOrEmpty(_rewardsUrl) && !string.IsNullOrEmpty(_gameId);

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

        // GET {rewardsPath}?xsollaId=…&gameId=…
        //
        // Что лежит в рюкзаке у вошедшего игрока. Та же гарантия, что у
        // SendEventAsync: исключения наружу не летят, любой сбой — Failed с
        // причиной. Коды, которые отдаёт сервер: 400 invalid_xsolla_id,
        // 404 unknown_game, 502 — сбой самого Backpack.
        public async Task<BackPackRewardsReply> GetRewardsAsync()
        {
            if (!IsRewardsAvailable)
                return BackPackRewardsReply.Failed("not_configured");

            string xsollaId = XsollaId;

            if (string.IsNullOrEmpty(xsollaId))
                return BackPackRewardsReply.Failed("no_xsolla_id");

            var url = new StringBuilder(_rewardsUrl)
                .Append("?xsollaId=").Append(UnityWebRequest.EscapeURL(xsollaId));

            Append(url, "gameId", _gameId);

            BackPackHttpReply raw;

            try
            {
                raw = await _getHandler(url.ToString());
            }
            catch (Exception e)
            {
                return BackPackRewardsReply.Failed($"transport: {e.Message}");
            }

            return ParseRewards(raw);
        }

        // Есть ли связь с сервером — до того, как вести игрока в рюкзак.
        //
        // Стучимся в ту же ручку рюкзака, только без xsollaID, а не в корень
        // сервера: у этой ручки CORS настроен наверняка (игра ходит в неё и так),
        // а без CORS браузер в WebGL уронил бы ответ, и живой сервер выглядел бы
        // недоступным. Без xsollaID сервер ответит 400 — это и есть «связь есть»:
        // важен сам факт ответа, а не его содержимое.
        //
        // Связи НЕТ, если ответа не было вовсе (сеть, таймаут) или код 5xx:
        // 502/522 отдаёт и Cloudflare, когда сервер за ним лежит, и сам сервер,
        // когда у него отвалился Backpack, — рюкзак в обоих случаях не загрузится.
        public async Task<BackPackConnectionResult> CheckConnectionAsync()
        {
            if (!IsRewardsAvailable)
                return BackPackConnectionResult.Failed("not_configured");

            var url = new StringBuilder(_rewardsUrl)
                .Append("?gameId=").Append(UnityWebRequest.EscapeURL(_gameId));

            BackPackHttpReply raw;

            try
            {
                raw = await _connectionCheckHandler(url.ToString());
            }
            catch (Exception e)
            {
                return BackPackConnectionResult.Failed($"transport: {e.Message}");
            }

            if (raw.IsTransportError)
                return BackPackConnectionResult.Failed($"transport: {raw.TransportError}");

            if (raw.StatusCode >= 500)
                return BackPackConnectionResult.Failed($"http_{raw.StatusCode}");

            return BackPackConnectionResult.Connected();
        }

        // Дальше — ручки удаления. Обе требуют xsollaID — удаляется всегда
        // прогресс конкретного игрока, никакого «удалить у всех» здесь нет и
        // быть не может.
        //
        // Ответ отдаём СЫРЫМ (BackPackHttpReply), а не разобранным: тело у них
        // не в форме BackPackEventReplyBody — это отчёт со списком строк, и
        // нужен он ровно для того, чтобы лечь в лог целиком. Заводить под него
        // DTO значило бы тащить форму отладочного отчёта в игровой код.

        // DELETE {itemsPath}?xsollaId=…[&sku=…][&itemId=…]
        //
        // Убирает предметы из рюкзака игрока. Без sku и itemId — весь рюкзак.
        // Игра зовёт её, когда игрок активирует предмет: активация и есть
        // трата, предмет уходит из рюкзака.
        public Task<BackPackHttpReply> DeleteBackpackItemsAsync(string sku = null, string itemId = null)
        {
            if (string.IsNullOrEmpty(_itemsUrl))
                return Task.FromResult(BackPackHttpReply.Transport("not_configured"));

            string xsollaId = XsollaId;

            if (string.IsNullOrEmpty(xsollaId))
                return Task.FromResult(BackPackHttpReply.Transport("no_xsolla_id"));

            var url = new StringBuilder(_itemsUrl)
                .Append("?xsollaId=").Append(UnityWebRequest.EscapeURL(xsollaId));

            Append(url, "sku", sku);
            Append(url, "itemId", itemId);

            return SendDeleteSafeAsync(url.ToString());
        }

        // Удалить из рюкзака ОДИН конкретный экземпляр — так игра тратит предмет.
        //
        // Только по itemId: по sku сервер сносит все предметы этого типа и
        // заодно чистит reward_claims (награду можно было бы получить заново).
        // С одним itemId ни того, ни другого не происходит.
        //
        // Удалён ли предмет, решает отчёт в теле, а не код ответа: сервер
        // отвечает 200 на любой исход (см. BackPackDeleteResult).
        public async Task<BackPackDeleteResult> DeleteItemAsync(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
                return BackPackDeleteResult.Failed("no_item_id");

            var raw = await DeleteBackpackItemsAsync(itemId: itemId);

            return ParseDelete(raw, itemId);
        }

        // DELETE /events?xsollaId=&gameId=…[&eventId=…][&sku=…]&backpack=&force=
        //
        // ОТЛАДОЧНАЯ: живёт на сервере только в среде Development (в остальных
        // отвечает 404). Убирает записи о выданных наградах — то, чем сервер защищается от
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

        private static string Combine(string baseUrl, string path) =>
            baseUrl == null || string.IsNullOrEmpty(path) ? null : baseUrl + path;

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

        private static BackPackDeleteResult ParseDelete(BackPackHttpReply raw, string itemId)
        {
            if (raw.IsTransportError)
                return BackPackDeleteResult.Failed($"transport: {raw.TransportError}");

            if (!raw.IsHttpSuccess)
                return BackPackDeleteResult.Failed($"http_{raw.StatusCode}: {raw.Body}");

            if (string.IsNullOrWhiteSpace(raw.Body))
                return BackPackDeleteResult.Failed($"http_{raw.StatusCode}_empty_body");

            BackPackDeleteReplyBody parsed;

            try
            {
                parsed = JsonConvert.DeserializeObject<BackPackDeleteReplyBody>(raw.Body);
            }
            catch (Exception e)
            {
                return BackPackDeleteResult.Failed($"bad_json: {e.Message}");
            }

            // Отчёта про НАШ предмет нет — значит, сервер его не трогал (не нашёл,
            // либо удалил что-то другое). Считать это удалением нельзя.
            if (parsed?.Items != null)
            {
                foreach (var entry in parsed.Items)
                {
                    if (entry == null || !string.Equals(entry.ItemId, itemId, StringComparison.Ordinal))
                        continue;

                    return string.Equals(entry.Outcome, BackPackDeleteResult.OutcomeDeleted, StringComparison.Ordinal)
                        ? BackPackDeleteResult.Deleted()
                        : BackPackDeleteResult.Failed(entry.Error ?? entry.Outcome ?? "no_outcome", entry.Outcome);
                }
            }

            return BackPackDeleteResult.Failed($"item_not_in_report: {raw.Body}");
        }

        private static BackPackRewardsReply ParseRewards(BackPackHttpReply raw)
        {
            if (raw.IsTransportError)
                return BackPackRewardsReply.Failed($"transport: {raw.TransportError}");

            // Код ошибки ("invalid_xsolla_id") сервер кладёт в тело — отдаём его
            // целиком: формы у тела ошибки мы не знаем, а в логе оно нужнее всего.
            if (!raw.IsHttpSuccess)
                return BackPackRewardsReply.Failed($"http_{raw.StatusCode}: {raw.Body}", raw.StatusCode);

            if (string.IsNullOrWhiteSpace(raw.Body))
                return BackPackRewardsReply.Failed($"http_{raw.StatusCode}_empty_body", raw.StatusCode);

            BackPackRewardsReplyBody parsed;

            try
            {
                parsed = JsonConvert.DeserializeObject<BackPackRewardsReplyBody>(raw.Body);
            }
            catch (Exception e)
            {
                return BackPackRewardsReply.Failed($"bad_json: {e.Message}", raw.StatusCode);
            }

            if (parsed == null)
                return BackPackRewardsReply.Failed($"http_{raw.StatusCode}_bad_json", raw.StatusCode);

            // Пустые элементы (null в массиве) отбрасываем здесь, чтобы экрану не
            // пришлось проверять каждый.
            var items = new List<BackPackRewardItem>();

            if (parsed.Items != null)
            {
                foreach (var item in parsed.Items)
                {
                    if (item != null)
                        items.Add(item);
                }
            }

            return BackPackRewardsReply.Ok(items);
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

        // Форма та же, что у SendDeleteAsync, только UnityWebRequest.Get
        // downloadHandler приносит сам.
        private static async Task<BackPackHttpReply> SendGetAsync(string url, int timeoutSeconds)
        {
            using var request = UnityWebRequest.Get(url);
            request.timeout = timeoutSeconds;
            request.SetRequestHeader("Accept", "application/json");

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();

            while (!operation.isDone)
                await Task.Yield();

            if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.DataProcessingError)
            {
                return BackPackHttpReply.Transport($"GET {url} — {request.error}");
            }

            return new BackPackHttpReply(request.responseCode, request.downloadHandler?.text, null);
        }

        // Тело ответа на DELETE /backpack/items: отчёт по каждому предмету.
        private sealed class BackPackDeleteReplyBody
        {
            [JsonProperty("items")] public BackPackDeleteReplyItem[] Items;
        }

        private sealed class BackPackDeleteReplyItem
        {
            [JsonProperty("itemId")] public string ItemId;
            [JsonProperty("outcome")] public string Outcome;
            [JsonProperty("error")] public string Error;
        }

        // Тело ответа на GET /rewards. Приватное, как и BackPackEventReplyBody:
        // наружу уходит BackPackRewardsReply.
        private sealed class BackPackRewardsReplyBody
        {
            [JsonProperty("xsollaId")] public string XsollaId;
            [JsonProperty("gameId")] public string GameId;
            [JsonProperty("items")] public BackPackRewardItem[] Items;
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
