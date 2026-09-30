using System;
using UnityEngine;

namespace LongLeap.Backpack
{
    // Какой из трёх наборов настроек берётся в этой сборке.
    //
    // Среда живёт в АССЕТЕ, а не в дефайне: адрес сервера — данные, и лежать
    // они должны там же, где остальные данные, иначе «куда сейчас уходят
    // события» пришлось бы читать в Player Settings. Дефайн (BACKPACK)
    // отвечает на другой вопрос — ходить ли на сервер вообще или сохранять
    // ачивки только локально, см. Assets/Editor/BackPackMenu.cs.
    //
    // Local отдельно от Development намеренно: это РАЗНЫЕ сервера, а не одна
    // среда с двумя адресами. Local — процесс на машине разработчика, его
    // видно только с неё; Development — общий стенд, по которому проверяют
    // сборку, и от прода он отличается только доменом.
    public enum BackPackEnvironment
    {
        Local,
        Development,
        Production
    }

    // Один набор настроек: куда стучаться и от имени какой игры.
    //
    // Отдельный класс, а не три тройки полей в ассете (_localBaseUrl,
    // _devBaseUrl, …): нормализация адреса и пути одна и та же, и написана
    // здесь один раз. Заодно среду нельзя «переключить наполовину» — набор
    // берётся целиком.
    [Serializable]
    public sealed class BackPackEndpoint
    {
        public const string DefaultEventsPath = "/events";

        [Tooltip("Адрес BackPack-сервера — схема, хост и порт, без пути. " +
            "Пустая строка это выключатель: события никуда не уходят, а ачивки " +
            "сохраняются как раньше, только локально.")]
        [SerializeField] private string _baseUrl;

        [Tooltip("Путь ручки, принимающей события. Ведущий слеш дописывается сам, " +
            "пустое поле означает путь по умолчанию — на пустой путь запрос ушёл бы " +
            "в корень сервера и тихо не дошёл бы до нужной ручки.")]
        [SerializeField] private string _eventsPath = DefaultEventsPath;

        [Tooltip("Идентификатор игры на стороне сервера — поле gameId в теле запроса. " +
            "Не совпадёт с зарегистрированным — сервер откажет, и ачивки перестанут " +
            "сохраняться вовсе (сохраняем только принятые).")]
        [SerializeField] private string _gameId;

        // Пустой конструктор нужен сериализации: поля ассета Unity заполняет
        // из YAML, но сам объект создаёт вызовом без аргументов.
        public BackPackEndpoint()
        {
        }

        // Второй — для значений по умолчанию у нового ассета и для тестов.
        public BackPackEndpoint(string baseUrl, string gameId, string eventsPath = DefaultEventsPath)
        {
            _baseUrl = baseUrl;
            _gameId = gameId;
            _eventsPath = eventsPath;
        }

        // Пробелы срезаем: адрес и id почти всегда приезжают копированием, и
        // случайный пробел на конце превратил бы адрес в битый, а причину
        // пришлось бы искать глазами в инспекторе.
        //
        // Пустое значение отдаём наружу как есть — это штатный выключатель, а
        // не повод подставить умолчание: подставленный адрес превратил бы
        // «отправку выключили» в «стучимся неизвестно куда».
        public string BaseUrl => _baseUrl?.Trim();

        public string GameId => _gameId?.Trim();

        // Путь, наоборот, нормализуем: пустой путь и путь без ведущего слеша —
        // не выключатель, а опечатка, и оба дали бы запрос не туда.
        public string EventsPath
        {
            get
            {
                string path = _eventsPath?.Trim();

                if (string.IsNullOrEmpty(path))
                    return DefaultEventsPath;

                return path[0] == '/' ? path : "/" + path;
            }
        }

        // Заполнено ли настолько, чтобы вообще отправлять.
        public bool IsConfigured => !string.IsNullOrEmpty(BaseUrl) && !string.IsNullOrEmpty(GameId);

        public override string ToString() =>
            $"baseUrl={(string.IsNullOrEmpty(BaseUrl) ? "none" : BaseUrl)}, " +
            $"eventsPath={EventsPath}, gameId={(string.IsNullOrEmpty(GameId) ? "none" : GameId)}";
    }

    // Настройки BackPack-сервера: куда отправлять события ачивок и от имени
    // какой игры.
    //
    // Лежит в Assets/Resources/Backend/ рядом с FirebaseConfig и
    // XsollaIdConfig и грузится тем же Resources.Load. Не ссылкой в
    // инспекторе намеренно: ссылку пришлось бы прописывать в сцене, и
    // забытая (или потерянная при мерже сцены) она тихо выключала бы
    // отправку — а адрес сервера, в отличие от префаба, к сцене никак не
    // привязан.
    //
    // Наборов настроек здесь ТРИ (local, dev, prod), активен один — см.
    // BackPackEnvironment. Переключать удобнее из меню BackPack/Required/
    // Environment: оно же и предупредит, если у выбранной среды пусто.
    //
    // Секретов здесь нет и быть не может: WebGL-бандл читается кем угодно,
    // так что и адрес, и gameId всё равно открыты. Право на награду
    // доказывает не эта строка, а xsollaID вошедшего игрока.
    [CreateAssetMenu(menuName = "Blood Red Snow/BackPack Config")]
    public sealed class BackPackConfigSO : ScriptableObject
    {
        // Единый путь для всех мест, которым нужен этот конфиг во время игры.
        public const string ResourcePath = "Backend/BackPackConfig";

        // Значения по умолчанию живут ЗДЕСЬ, и только здесь. Раньше это были
        // константы BackPackService — то есть сервис знал адрес сервера,
        // хотя настройка его не касается: его дело отправить и разобрать
        // ответ.
        //
        // Действуют только на СОЗДАНИЕ ассета: у существующего поля приезжают
        // из YAML и об этих строках ничего не знают.
        public const string DefaultEventsPath = BackPackEndpoint.DefaultEventsPath;

        public const string DefaultLocalBaseUrl = "http://127.0.0.1:5066";

        // Стенд и прод — по ДОМЕНУ и только по https. Оба имени ведут на один
        // адрес (54.195.18.127), но ходить по нему напрямую нельзя: наружу
        // открыты только 80 и 443 на Cloudflare, а он выбирает сервис по
        // имени хоста и держит сертификат. По IP запрос либо не дойдёт, либо
        // упрётся в ошибку TLS.
        public const string DefaultDevelopmentBaseUrl = "https://api-dev-blood.longtalegames.net";
        public const string DefaultProductionBaseUrl = "https://api-blood.longtalegames.net";

        public const string DefaultGameId = "game_01";

        [Header("Environment")]
        [Tooltip("Какой набор настроек уедет в сборку. Переключается из меню " +
            "BackPack / Required / Environment.")]
        [SerializeField] private BackPackEnvironment _environment = BackPackEnvironment.Local;

        [Header("Local")]
        [Tooltip("Сервер, поднятый на машине разработчика. Из сборки на площадке " +
            "недоступен — это адрес для проверки в редакторе и локальной сборки.")]
        [SerializeField] private BackPackEndpoint _local =
            new BackPackEndpoint(DefaultLocalBaseUrl, DefaultGameId);

        [Header("Development")]
        [Tooltip("Общий стенд. Только https: наружу открыт лишь Cloudflare.")]
        [SerializeField] private BackPackEndpoint _development =
            new BackPackEndpoint(DefaultDevelopmentBaseUrl, DefaultGameId);

        [Header("Production")]
        [Tooltip("Боевой сервер. Тот же домен, что у стенда, без -dev.")]
        [SerializeField] private BackPackEndpoint _production =
            new BackPackEndpoint(DefaultProductionBaseUrl, DefaultGameId);

        public BackPackEnvironment Environment => _environment;

        // Все три отдаём наружу — их читают проверки ассета: неактивная
        // сегодня среда становится активной одним пунктом меню, и пустой она
        // к тому времени быть не должна.
        public BackPackEndpoint Local => _local ?? new BackPackEndpoint();

        public BackPackEndpoint Development => _development ?? new BackPackEndpoint();

        public BackPackEndpoint Production => _production ?? new BackPackEndpoint();

        // Набор, который реально уедет в сборку.
        public BackPackEndpoint Active
        {
            get
            {
                switch (_environment)
                {
                    case BackPackEnvironment.Production: return Production;
                    case BackPackEnvironment.Development: return Development;
                    default: return Local;
                }
            }
        }

        // Дальше — то же, чем пользовался GameBootstrapper до появления сред:
        // ему по-прежнему незачем знать, что наборов три.
        public string BaseUrl => Active.BaseUrl;

        public string GameId => Active.GameId;

        public string EventsPath => Active.EventsPath;

        // Заполнено ли настолько, чтобы вообще отправлять. Отдельно от
        // BackPackService.IsAvailable: тот отвечает про собранный сервис, а
        // это вопрос к ассету — на него отвечает тест, не поднимая сервис.
        public bool IsConfigured => Active.IsConfigured;

        public override string ToString() => $"BackPackConfig({_environment}: {Active})";
    }
}
