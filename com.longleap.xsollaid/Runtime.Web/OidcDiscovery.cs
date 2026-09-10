using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace LongLeap.XsollaId.Web
{
    // Загрузка /.well-known/openid-configuration с кэшем.
    //
    // Пути эндпоинтов НЕ зашиты намеренно: у Xsolla ID они сейчас переезжают
    // из /api/oauth2/* в корень (ADR принят к реализации, оба пути какое-то
    // время живут параллельно, потом старый выключают). Зашить сегодняшний
    // путь — значит сломаться на выключении.
    //
    // Но и доверять документу слепо нельзя: он дважды расходился с
    // реальностью в проде. На момент написания прод отдаёт authorize/token/
    // userinfo под /api/oauth2/*, а revocation_endpoint и end_session_endpoint
    // — в корне, то есть ровно те два, которые ломались. Отсюда два решения:
    // отзыв токена best-effort с попыткой второго пути, а end_session не
    // используется вообще.
    public sealed class OidcDiscovery : IOidcDiscovery
    {
        private readonly string _discoveryUrl;
        private readonly XsollaIdHttp _http;

        // Кэшируем саму задачу: параллельные вызовы получают одну загрузку, а
        // повторные — готовый результат. Прогрев на старте нужен, чтобы к
        // моменту клика адрес уже был известен: сходить за ним между кликом и
        // window.open нельзя — браузер сочтёт окно непрошеным.
        private Task<OidcConfiguration> _inFlight;

        public OidcDiscovery(string discoveryUrl, XsollaIdHttp http = null)
        {
            if (string.IsNullOrEmpty(discoveryUrl))
                throw new ArgumentException("OidcDiscovery: the discovery address is not set.", nameof(discoveryUrl));

            _discoveryUrl = discoveryUrl;
            _http = http ?? new XsollaIdHttp();
        }

        public Task<OidcConfiguration> GetAsync()
        {
            // Провалившуюся попытку не кэшируем: сеть могла просто мигнуть.
            if (_inFlight != null && !IsFailedAttempt(_inFlight))
                return _inFlight;

            _inFlight = LoadAsync();

            return _inFlight;
        }

        // Одного IsFaulted мало. LoadAsync и Parse гасят свои отказы внутри и
        // возвращают null штатно, поэтому провал выглядит как успешно
        // завершённая задача с пустым результатом. Без проверки на null один
        // запрос, срезанный CORS'ом или просто моргнувшей сетью на прогреве,
        // убивал вход до конца сессии: каждый следующий клик доставал из кэша
        // тот же null и падал с NotConfigured, ни разу не сходив в сеть.
        //
        // Result читаем только у успешно завершённой задачи — там он уже готов
        // и не бросает.
        private static bool IsFailedAttempt(Task<OidcConfiguration> attempt) =>
            attempt.IsFaulted ||
            attempt.IsCanceled ||
            (attempt.Status == TaskStatus.RanToCompletion && attempt.Result == null);

        private async Task<OidcConfiguration> LoadAsync()
        {
            HttpReply reply = await _http.GetAsync(_discoveryUrl);

            if (!reply.IsSuccess)
            {
                XsollaIdHttp.LogWarning(reply.IsTransportError
                    ? $"discovery unavailable — {reply.TransportError}"
                    : $"discovery responded {reply.StatusCode}");

                return null;
            }

            return Parse(reply.Body, _discoveryUrl);
        }

        // Разбор вынесен наружу и без сети — так он проверяется тестами на
        // реальных ответах, включая заведомо кривые.
        public static OidcConfiguration Parse(string json, string expectedDiscoveryUrl = null)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                JObject o = JObject.Parse(json);

                var config = new OidcConfiguration(
                    (string)o["issuer"],
                    (string)o["authorization_endpoint"],
                    (string)o["token_endpoint"],
                    (string)o["userinfo_endpoint"],
                    (string)o["revocation_endpoint"]);

                if (!config.IsUsable)
                {
                    XsollaIdHttp.LogWarning("discovery did not return authorization_endpoint or token_endpoint.");
                    return null;
                }

                // Эндпоинты обязаны жить на том же хосте, что и сам документ.
                // Подменённый (или просто неверно настроенный) discovery,
                // указывающий на чужой хост, увёл бы код авторизации на
                // сторону — это дешёвая проверка против дорогой ошибки.
                if (!SameHost(expectedDiscoveryUrl, config.AuthorizationEndpoint) ||
                    !SameHost(expectedDiscoveryUrl, config.TokenEndpoint))
                {
                    XsollaIdHttp.LogWarning(
                        "discovery points its endpoints at a different host — document rejected.");
                    return null;
                }

                return config;
            }
            catch (Exception ex)
            {
                XsollaIdHttp.LogWarning($"discovery could not be parsed: {ex.Message}");
                return null;
            }
        }

        // Пустой expected — проверять не с чем, пропускаем.
        private static bool SameHost(string expected, string candidate)
        {
            if (string.IsNullOrEmpty(expected))
                return true;

            if (!Uri.TryCreate(expected, UriKind.Absolute, out Uri a) ||
                !Uri.TryCreate(candidate, UriKind.Absolute, out Uri b))
            {
                return false;
            }

            return string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase);
        }
    }
}
