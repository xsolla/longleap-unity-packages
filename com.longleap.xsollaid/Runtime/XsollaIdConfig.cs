using System;
using System.Collections.Generic;

namespace LongLeap.XsollaId
{
    public enum XsollaIdEnvironment
    {
        Stage,
        Production
    }

    // Регистрация OAuth-клиента для одной среды.
    //
    // Stage и Production — РАЗНЫЕ регистрации: свой clientId, свой список
    // redirect uri. Поэтому храним их раздельно, а не подставляем один
    // clientId к разным хостам.
    public sealed class XsollaIdClientRegistration
    {
        // Например https://id.xsolla.com — без завершающего слэша.
        public string IssuerBaseUrl { get; }

        // Публичный клиент (секрета в сборке нет и быть не может).
        public string ClientId { get; }

        // Должен СИМВОЛ В СИМВОЛ совпадать с зарегистрированным: протокол,
        // хост, порт, путь. Несовпадение — отказ на стороне сервера.
        public string RedirectUri { get; }

        public XsollaIdClientRegistration(string issuerBaseUrl, string clientId, string redirectUri)
        {
            IssuerBaseUrl = issuerBaseUrl?.TrimEnd('/');
            ClientId = clientId;
            RedirectUri = redirectUri;
        }

        public bool IsValid =>
            !string.IsNullOrEmpty(IssuerBaseUrl) &&
            !string.IsNullOrEmpty(ClientId) &&
            !string.IsNullOrEmpty(RedirectUri);

        public string DiscoveryUrl => $"{IssuerBaseUrl}/.well-known/openid-configuration";
    }

    public sealed class XsollaIdConfig
    {
        public const string DefaultStageIssuer = "https://id-staging.xsolla.com";
        public const string DefaultProductionIssuer = "https://id.xsolla.com";

        // openid обязателен; profile/email нужны, чтобы в UI было что
        // показать кроме непрозрачного sub.
        //
        // offline — то, ради чего игроку не приходится входить заново после
        // перезагрузки страницы: именно этот scope заставляет выдать refresh
        // token.
        //
        // ВНИМАНИЕ: здесь НЕ offline_access. Discovery прода объявляет оба
        // (scopes_supported: offline_access, offline, openid, profile, email,
        // phone), но дизайн-док Xsolla ID «Backpack Cat Integration with XPWA»
        // (Confluence 24612667695, сверено с живым клиентом SquadBlast в
        // админке Xsolla ID 18.05.2026) прямо говорит: на этом инстансе Hydra
        // refresh token выдаёт именно offline. Более того, если наш клиент
        // зарегистрирован на offline, запрос offline_access может отлететь как
        // invalid_scope — то есть сломать вход целиком, а не только refresh.
        public const string DefaultScopes = "openid offline email profile";

        public XsollaIdEnvironment Environment { get; }
        public XsollaIdClientRegistration Stage { get; }
        public XsollaIdClientRegistration Production { get; }
        public string Scopes { get; }

        // Origin'ы, с которых вход имеет смысл открывать.
        //
        // Отдельно от redirect uri, потому что совпадать они не обязаны: в
        // портальной сборке игру отдаёт GamePix со своего origin'а, а
        // страница обратного вызова может лежать на нашем домене. Пустой
        // список = не проверяем.
        public IReadOnlyList<string> AllowedGameOrigins { get; }

        public XsollaIdConfig(
            XsollaIdEnvironment environment,
            XsollaIdClientRegistration stage,
            XsollaIdClientRegistration production,
            string scopes = DefaultScopes,
            IReadOnlyList<string> allowedGameOrigins = null)
        {
            Environment = environment;
            Stage = stage;
            Production = production;
            Scopes = string.IsNullOrEmpty(scopes) ? DefaultScopes : scopes;
            AllowedGameOrigins = allowedGameOrigins ?? Array.Empty<string>();
        }

        public XsollaIdClientRegistration Active =>
            Environment == XsollaIdEnvironment.Production ? Production : Stage;

        public bool IsValid => Active != null && Active.IsValid;

        // Копия с подменённым redirect_uri у АКТИВНОЙ регистрации.
        //
        // Нужна потому, что адрес страницы обратного вызова у сборок, которые
        // отдаём мы сами, вычисляется в рантайме (см. XsollaIdRedirect), а не
        // лежит в ассете: одно поле на три хоста означало бы правку ассета
        // перед каждой сборкой.
        //
        // Копия, а не запись на месте: конфиг неизменяем, и его читают из двух
        // мест (адрес авторизации и обмен кода) — подмена под ними разъехалась
        // бы с тем, что уже ушло на сервер.
        //
        // Пустой redirectUri возвращает конфиг как есть: «вычислить не удалось»
        // означает «оставьте значение из ассета», а не «сотрите его».
        public XsollaIdConfig WithRedirectUri(string redirectUri)
        {
            if (string.IsNullOrEmpty(redirectUri) || Active == null)
                return this;

            var replaced = new XsollaIdClientRegistration(
                Active.IssuerBaseUrl, Active.ClientId, redirectUri);

            return Environment == XsollaIdEnvironment.Production
                ? new XsollaIdConfig(Environment, Stage, replaced, Scopes, AllowedGameOrigins)
                : new XsollaIdConfig(Environment, replaced, Production, Scopes, AllowedGameOrigins);
        }
    }
}
