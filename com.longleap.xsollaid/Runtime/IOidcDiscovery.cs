using System.Threading.Tasks;

namespace LongLeap.XsollaId
{
    // Адреса эндпоинтов авторизационного сервера.
    //
    // Пути НЕ зашиты в код намеренно. У Xsolla ID эндпоинты Hydra сейчас
    // отдаются под /api/oauth2/*, а документ discovery объявляет их в корне
    // (/oauth2/*); принятое к реализации решение переносит всё в корень,
    // какое-то время держит оба пути и затем /api/oauth2/* выключает.
    // Зашить сегодняшний путь — значит гарантированно сломаться на выключении.
    public sealed class OidcConfiguration
    {
        public string Issuer { get; }
        public string AuthorizationEndpoint { get; }
        public string TokenEndpoint { get; }
        public string UserInfoEndpoint { get; }
        public string RevocationEndpoint { get; }

        public OidcConfiguration(
            string issuer,
            string authorizationEndpoint,
            string tokenEndpoint,
            string userInfoEndpoint,
            string revocationEndpoint)
        {
            Issuer = issuer;
            AuthorizationEndpoint = authorizationEndpoint;
            TokenEndpoint = tokenEndpoint;
            UserInfoEndpoint = userInfoEndpoint;
            RevocationEndpoint = revocationEndpoint;
        }

        // Минимум, без которого вход невозможен. userinfo и revoke
        // необязательны: без первого просто не будет имени в UI, без второго
        // выход останется локальным.
        public bool IsUsable =>
            !string.IsNullOrEmpty(AuthorizationEndpoint) && !string.IsNullOrEmpty(TokenEndpoint);
    }

    // Загрузка /.well-known/openid-configuration.
    //
    // Результат обязателен к КЭШИРОВАНИЮ и к загрузке заранее, на старте: если
    // сходить за ним между кликом игрока и window.open, окно заблокирует
    // браузер — жест пользователя к тому моменту уже «протухнет».
    //
    // Документ у Xsolla ID дважды расходился с реальностью в проде
    // (end_session_endpoint отдавал страницу логина, revocation_endpoint —
    // 405), поэтому пришедшее надо проверять, а не принимать на веру.
    public interface IOidcDiscovery
    {
        Task<OidcConfiguration> GetAsync();
    }
}
