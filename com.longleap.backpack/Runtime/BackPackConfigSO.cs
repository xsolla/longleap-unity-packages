using System;
using UnityEngine;

namespace LongLeap.Backpack
{
    // Which of the three settings sets this build uses.
    //
    // The environment lives in the ASSET, not in a define: the server address is
    // data, and it belongs with the rest of the data — otherwise "where do events
    // go right now" would have to be read from Player Settings.
    //
    // Local is separate from Development on purpose: they are DIFFERENT servers,
    // not one environment with two addresses. Local is a process on the
    // developer's machine, visible only from it; Development is the shared staging
    // server builds are tested against, and it differs from production only by domain.
    public enum BackPackEnvironment
    {
        Local,
        Development,
        Production
    }

    // One settings set: where to send requests and on behalf of which game.
    //
    // A separate class rather than three triples of fields in the asset
    // (_localBaseUrl, _devBaseUrl, …): address and path normalization is the same
    // and written here once. It also makes it impossible to "half-switch" the
    // environment — the set is taken whole.
    [Serializable]
    public sealed class BackPackEndpoint
    {
        public const string DefaultEventsPath = "/events";
        public const string DefaultRewardsPath = "/rewards";
        public const string DefaultItemsPath = "/backpack/items";

        [Tooltip("BackPack server address — scheme, host and port, no path. " +
            "An empty string is the off switch: no events are sent, and achievements " +
            "are saved as before, locally only.")]
        [SerializeField] private string _baseUrl;

        [Tooltip("Path of the endpoint that accepts events. The leading slash is added " +
            "automatically; an empty field means the default path — with an empty path the " +
            "request would go to the server root and silently miss the endpoint.")]
        [SerializeField] private string _eventsPath = DefaultEventsPath;

        [Tooltip("Path of the endpoint that returns the player's backpack: " +
            "GET {baseUrl}{rewardsPath}?xsollaId=…&gameId=…. The leading slash is added " +
            "automatically; an empty field means the default path.")]
        [SerializeField] private string _rewardsPath = DefaultRewardsPath;

        [Tooltip("Path of the endpoint that deletes backpack items (this is how an item is " +
            "spent on activation): DELETE {baseUrl}{itemsPath}?xsollaId=…&sku=…. The leading " +
            "slash is added automatically; an empty field means the default path.")]
        [SerializeField] private string _itemsPath = DefaultItemsPath;

        [Tooltip("Game id on the server side — the gameId field of the request body. " +
            "If it does not match a registered one, the server refuses, and achievements " +
            "stop being saved at all (only accepted ones are saved).")]
        [SerializeField] private string _gameId;

        // The parameterless constructor is needed by serialization: Unity fills the
        // asset's fields from YAML, but creates the object itself with no arguments.
        public BackPackEndpoint()
        {
        }

        // The second one is for a new asset's defaults and for tests.
        public BackPackEndpoint(
            string baseUrl,
            string gameId,
            string eventsPath = DefaultEventsPath,
            string rewardsPath = DefaultRewardsPath,
            string itemsPath = DefaultItemsPath)
        {
            _baseUrl = baseUrl;
            _gameId = gameId;
            _eventsPath = eventsPath;
            _rewardsPath = rewardsPath;
            _itemsPath = itemsPath;
        }

        // Whitespace is trimmed: the address and id almost always arrive by
        // copy-paste, and a stray trailing space would break the address, with the
        // cause to be hunted down by eye in the inspector.
        //
        // An empty value is returned as is — it is the regular off switch, not a
        // reason to substitute a default: a substituted address would turn "sending
        // is off" into "sending somewhere unknown".
        public string BaseUrl => _baseUrl?.Trim();

        public string GameId => _gameId?.Trim();

        // Paths, on the contrary, are normalized: an empty path or one without a
        // leading slash is not a switch but a typo, and both would send the request
        // to the wrong place.
        public string EventsPath => NormalizePath(_eventsPath, DefaultEventsPath);

        public string RewardsPath => NormalizePath(_rewardsPath, DefaultRewardsPath);

        public string ItemsPath => NormalizePath(_itemsPath, DefaultItemsPath);

        // Whether it is filled in enough to send anything at all.
        public bool IsConfigured => !string.IsNullOrEmpty(BaseUrl) && !string.IsNullOrEmpty(GameId);

        public override string ToString() =>
            $"baseUrl={(string.IsNullOrEmpty(BaseUrl) ? "none" : BaseUrl)}, " +
            $"eventsPath={EventsPath}, rewardsPath={RewardsPath}, itemsPath={ItemsPath}, " +
            $"gameId={(string.IsNullOrEmpty(GameId) ? "none" : GameId)}";

        private static string NormalizePath(string value, string fallback)
        {
            string path = value?.Trim();

            if (string.IsNullOrEmpty(path))
                return fallback;

            return path[0] == '/' ? path : "/" + path;
        }
    }

    // BackPack server settings: where to send achievement events and on behalf of
    // which game.
    //
    // Lives in Assets/Resources/Backend/ next to FirebaseConfig and XsollaIdConfig
    // and is loaded with the same Resources.Load. Deliberately not an inspector
    // reference: the reference would have to be set in the scene, and a forgotten
    // one (or one lost in a scene merge) would silently switch sending off — while
    // the server address, unlike a prefab, has nothing to do with the scene.
    //
    // There are THREE settings sets here (local, dev, prod), one is active — see
    // BackPackEnvironment. Switching is easiest from the BackPack/Required/
    // Environment menu: it also warns when the chosen environment is empty.
    //
    // There are no secrets here and there cannot be: anyone can read a WebGL
    // bundle, so the address and gameId are public anyway. The right to a reward
    // is proven not by this string but by the signed-in player's xsollaID.
    [CreateAssetMenu(menuName = "LongLeap Services/BackPack Config")]
    public sealed class BackPackConfigSO : ScriptableObject
    {
        // The single path for every place that needs this config at runtime.
        public const string ResourcePath = "Backend/BackPackConfig";

        // Defaults live HERE and only here. They used to be BackPackService
        // constants — that is, the service knew the server address, even though
        // configuration is none of its business: its job is to send and parse.
        //
        // They apply only when the asset is CREATED: an existing asset's fields come
        // from YAML and know nothing about these strings.
        public const string DefaultEventsPath = BackPackEndpoint.DefaultEventsPath;

        public const string DefaultRewardsPath = BackPackEndpoint.DefaultRewardsPath;

        public const string DefaultItemsPath = BackPackEndpoint.DefaultItemsPath;

        public const string DefaultLocalBaseUrl = "http://127.0.0.1:5066";

        // Staging and production have no default: they are the game's own servers,
        // set in the asset. Use the https DOMAIN name, not an IP address — a server
        // behind a CDN/proxy picks the service by host name and holds the certificate,
        // so a request by IP either does not arrive or hits a TLS error. The asset
        // checks report an empty address before it reaches a build.
        public const string DefaultDevelopmentBaseUrl = "";
        public const string DefaultProductionBaseUrl = "";

        public const string DefaultGameId = "game_01";

        [Header("Environment")]
        [Tooltip("Which settings set goes into the build. Switched from the " +
            "BackPack / Required / Environment menu.")]
        [SerializeField] private BackPackEnvironment _environment = BackPackEnvironment.Local;

        [Header("Local")]
        [Tooltip("A server running on the developer's machine. Unreachable from a build " +
            "on a platform — this address is for testing in the Editor and local builds.")]
        [SerializeField] private BackPackEndpoint _local =
            new BackPackEndpoint(DefaultLocalBaseUrl, DefaultGameId);

        [Header("Development")]
        [Tooltip("The shared staging server. https domain name, not an IP address.")]
        [SerializeField] private BackPackEndpoint _development =
            new BackPackEndpoint(DefaultDevelopmentBaseUrl, DefaultGameId);

        [Header("Production")]
        [Tooltip("The production server. https domain name, not an IP address.")]
        [SerializeField] private BackPackEndpoint _production =
            new BackPackEndpoint(DefaultProductionBaseUrl, DefaultGameId);

        public BackPackEnvironment Environment => _environment;

        // All three are exposed — asset checks read them: an environment inactive
        // today becomes active with one menu item, and it must not be empty by then.
        public BackPackEndpoint Local => _local ?? new BackPackEndpoint();

        public BackPackEndpoint Development => _development ?? new BackPackEndpoint();

        public BackPackEndpoint Production => _production ?? new BackPackEndpoint();

        // The set that actually goes into the build.
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

        // Below — the active environment's values: whoever builds the service
        // (BackPackTask) has no need to know there are three sets.
        public string BaseUrl => Active.BaseUrl;

        public string GameId => Active.GameId;

        public string EventsPath => Active.EventsPath;

        public string RewardsPath => Active.RewardsPath;

        public string ItemsPath => Active.ItemsPath;

        // Whether it is filled in enough to send anything at all. Separate from
        // BackPackService.IsAvailable: that one is about the built service, while
        // this is a question to the asset — a test answers it without building the
        // service.
        public bool IsConfigured => Active.IsConfigured;

        public override string ToString() => $"BackPackConfig({_environment}: {Active})";
    }
}
