using System;
using UnityEngine;

namespace LongLeap.Quests
{
    // Which of the three settings sets this build uses — same scheme as BackPack.
    public enum QuestEnvironment
    {
        Local,
        Development,
        Production
    }

    // One settings set: the Quests service address and the game id. The routes
    // themselves (/v1/...) are fixed by the service, so only the host lives here.
    [Serializable]
    public sealed class QuestEndpoint
    {
        [Tooltip("Quests service address — scheme, host and port, no path. " +
            "An empty string switches sending off.")]
        [SerializeField] private string _baseUrl;

        [Tooltip("Game id. Only namespaces the idempotency key so it never matches another game's.")]
        [SerializeField] private string _gameId;

        public QuestEndpoint()
        {
        }

        public QuestEndpoint(string baseUrl, string gameId)
        {
            _baseUrl = baseUrl;
            _gameId = gameId;
        }

        // Trimmed: the address almost always arrives by copy-paste.
        public string BaseUrl => _baseUrl?.Trim().TrimEnd('/');

        public string GameId => _gameId?.Trim();

        public bool IsConfigured => !string.IsNullOrEmpty(BaseUrl) && !string.IsNullOrEmpty(GameId);

        public override string ToString() =>
            $"baseUrl={(string.IsNullOrEmpty(BaseUrl) ? "none" : BaseUrl)}, " +
            $"gameId={(string.IsNullOrEmpty(GameId) ? "none" : GameId)}";
    }

    // Quests service settings. Lives in Assets/Resources/Backend/ next to
    // BackPackConfig and is loaded the same way (Resources.Load). A separate asset:
    // Quests do not depend on BackPack settings and change independently.
    [CreateAssetMenu(menuName = "LongLeap Services/Quest Config")]
    public sealed class QuestConfigSO : ScriptableObject
    {
        public const string ResourcePath = "Backend/QuestConfig";

        // Defaults apply only when the asset is CREATED.
        public const string DefaultLocalBaseUrl = "http://127.0.0.1:8090";
        public const string DefaultDevelopmentBaseUrl = "https://api-dev-darkslide.longtalegames.net";
        public const string DefaultProductionBaseUrl = "https://api-darkslide.longtalegames.net";
        public const string DefaultGameId = "Darksliders";

        [Header("Environment")]
        [Tooltip("Which settings set goes into the build.")]
        [SerializeField] private QuestEnvironment _environment = QuestEnvironment.Local;

        [Header("Local")]
        [Tooltip("The service on the developer's machine (port 8090 from its README).")]
        [SerializeField] private QuestEndpoint _local = new QuestEndpoint(DefaultLocalBaseUrl, DefaultGameId);

        [Header("Development")]
        [Tooltip("The shared staging server. https only.")]
        [SerializeField] private QuestEndpoint _development = new QuestEndpoint(DefaultDevelopmentBaseUrl, DefaultGameId);

        [Header("Production")]
        [Tooltip("The production server.")]
        [SerializeField] private QuestEndpoint _production = new QuestEndpoint(DefaultProductionBaseUrl, DefaultGameId);

        public QuestEnvironment Environment => _environment;

        public QuestEndpoint Local => _local ?? new QuestEndpoint();

        public QuestEndpoint Development => _development ?? new QuestEndpoint();

        public QuestEndpoint Production => _production ?? new QuestEndpoint();

        // The set that actually goes into the build.
        public QuestEndpoint Active
        {
            get
            {
                switch (_environment)
                {
                    case QuestEnvironment.Production: return Production;
                    case QuestEnvironment.Development: return Development;
                    default: return Local;
                }
            }
        }

        public bool IsConfigured => Active.IsConfigured;

        public override string ToString() => $"QuestConfig({_environment}: {Active})";
    }
}
