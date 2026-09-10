using UnityEngine;

namespace LongLeap.XsollaId
{
    // Конфиг входа через Xsolla ID. Кладётся в Assets/Resources/Backend/
    // рядом с FirebaseConfig и грузится через Resources.Load.
    //
    // Всё здесь — ПУБЛИЧНЫЕ идентификаторы, их не стыдно коммитить: clientId
    // задуман открытым, потому что клиент публичный (WebGL-бандл читается
    // кем угодно). Секрета клиента здесь нет и быть не может — вместо него
    // подлинность запроса доказывает PKCE.
    [CreateAssetMenu(menuName = "LongLeap Services/Xsolla ID Config")]
    public sealed class XsollaIdConfigSO : ScriptableObject
    {
        [Header("Environment")]
        [Tooltip("Which registration this build uses. Stage and Production " +
                 "are DIFFERENT clients with different clientIds.")]
        [SerializeField] private XsollaIdEnvironment _environment = XsollaIdEnvironment.Stage;

        [Header("Stage")]
        [SerializeField] private string _stageIssuerBaseUrl = XsollaIdConfig.DefaultStageIssuer;
        [SerializeField] private string _stageClientId;

        [Tooltip("Must match the registered one CHARACTER FOR CHARACTER: " +
                 "scheme, host, port, path. Locally this is the address " +
                 "serve.py prints.")]
        [SerializeField] private string _stageRedirectUri = "http://127.0.0.1:8081/xid-callback.html";

        [Header("Production")]
        [SerializeField] private string _productionIssuerBaseUrl = XsollaIdConfig.DefaultProductionIssuer;
        [SerializeField] private string _productionClientId;
        [SerializeField] private string _productionRedirectUri;

        [Header("Scopes")]
        [Tooltip("Space-separated. offline (NOT offline_access) is the scope " +
                 "that makes Xsolla ID issue a refresh token — without it the " +
                 "player signs in again after every reload.")]
        [SerializeField] private string _scopes = XsollaIdConfig.DefaultScopes;

        [Header("Allowed game origins")]
        [Tooltip("Which addresses may open sign-in at all. They need NOT match " +
                 "the redirect uri: in the portal build GamePix serves the game " +
                 "from its own origin. Empty list — do not check.")]
        [SerializeField]
        private string[] _allowedGameOrigins =
        {
            "http://127.0.0.1:8081"
        };

        public XsollaIdConfig ToConfig() => new XsollaIdConfig(
            _environment,
            new XsollaIdClientRegistration(_stageIssuerBaseUrl, _stageClientId, _stageRedirectUri),
            new XsollaIdClientRegistration(_productionIssuerBaseUrl, _productionClientId, _productionRedirectUri),
            _scopes,
            _allowedGameOrigins);
    }
}
