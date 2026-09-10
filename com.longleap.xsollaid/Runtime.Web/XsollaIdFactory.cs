using LongLeap.Core;
using UnityEngine;

namespace LongLeap.XsollaId.Web
{
    // Сборка всех частей входа в одном месте.
    //
    // Нужна, чтобы в игровом коде остался ровно один #if XSOLLA_ID: без
    // фабрики GameBootstrapper пришлось бы упоминать четыре типа из
    // заблокированной дефайном сборки, и каждый пришлось бы огораживать
    // отдельно. Тот же приём, что у AnalyticsBootstrapper с выбором бэкенда
    // GA4 под #if UNITY_WEBGL.
    public static class XsollaIdFactory
    {
        // Возвращает рабочий вход либо заглушку, если конфиг не заполнен.
        //
        // Заглушка вместо ошибки намеренно: незаполненный clientId — обычное
        // состояние ветки, где регистрацию ещё не получили, и падать из-за
        // этого игра не должна. UI просто спрячет блок аккаунта.
        // deriveRedirectFromPage — отдаёт ли страницу с игрой тот же адрес, где
        // лежит страница обратного вызова. Так у всякой сборки, которую отдаём
        // мы сами (serve.py, Pages, объектное хранилище), redirect_uri получается
        // сам, без правки ассета перед сборкой. Портальная сборка передаёт false:
        // там игру отдаёт площадка, положить туда свою страницу нельзя, и адрес
        // берётся из ассета — см. XsollaIdRedirect.
        public static IXsollaIdAuth Create(
            XsollaIdConfigSO configAsset, IPlayerPrefs prefs, bool deriveRedirectFromPage = true)
        {
            if (configAsset == null || prefs == null)
                return new UnavailableXsollaIdAuth();

            XsollaIdConfig config = configAsset.ToConfig();

            // Логируем выбор адреса подробно и всегда, во всех четырёх исходах.
            //
            // Причина простая: отказ по redirect_uri приходит С СЕРВЕРА, уже
            // после открытия окна, и в невнятной формулировке — а разобраться в
            // нём можно только зная, какой именно адрес ушёл и откуда он взялся.
            // Раньше молчали три исхода из четырёх, и «взяли из ассета» в логе
            // выглядело неотличимо от «вычислили».
            string pageUrl = Application.absoluteURL;
            string assetRedirect = config.Active?.RedirectUri;

            if (deriveRedirectFromPage)
            {
                string derived = XsollaIdRedirect.TryDerive(pageUrl, out string problem);

                config = config.WithRedirectUri(derived);

                if (derived != null)
                {
                    Debug.Log($"{nameof(XsollaIdFactory)}: redirect_uri derived from the game's own address — " +
                        $"page '{pageUrl}' gives '{derived}' (the asset says '{assetRedirect}'). " +
                        "It must be registered in the Xsolla ID client character for character.");
                }
                else if (Application.isEditor)
                {
                    // В редакторе входа нет вовсе (у окна IsSupported == false),
                    // поэтому это обычный лог, а не предупреждение: иначе оно
                    // сыпалось бы на каждый запуск Play Mode.
                    Debug.Log($"{nameof(XsollaIdFactory)}: redirect_uri not derived — {problem}. " +
                        $"Falling back to the asset value '{assetRedirect}'. Expected in the editor.");
                }
                else
                {
                    Debug.LogWarning($"{nameof(XsollaIdFactory)}: redirect_uri NOT derived — {problem}. " +
                        $"Falling back to the asset value '{assetRedirect}', which is the address sign-in " +
                        "will send. Check that it is the one registered for this host.");
                }
            }
            else
            {
                Debug.Log($"{nameof(XsollaIdFactory)}: portal build — the platform serves the game " +
                    $"(page '{pageUrl}'), so the callback page cannot live next to it. redirect_uri comes " +
                    $"from the config asset — '{assetRedirect}'. That address must serve xid-callback.html " +
                    "and list this page's origin in its own ALLOWED_GAME_ORIGINS.");
            }

            if (!config.IsValid)
            {
                // Молчать нельзя: наружу уйдёт заглушка, UI спрячет блок
                // аккаунта, и «входа просто нет» ничем не объяснится.
                Debug.LogWarning($"{nameof(XsollaIdFactory)}: sign-in is unavailable — the active " +
                    $"{config.Environment} registration is incomplete (clientId " +
                    $"'{config.Active?.ClientId}', redirect_uri '{config.Active?.RedirectUri}', issuer " +
                    $"'{config.Active?.IssuerBaseUrl}'). Fill it in Resources/Backend/XsollaIdConfig.");

                return new UnavailableXsollaIdAuth();
            }

            Debug.Log($"{nameof(XsollaIdFactory)}: sign-in ready — environment {config.Environment}, " +
                $"issuer '{config.Active.IssuerBaseUrl}', effective redirect_uri '{config.Active.RedirectUri}'.");

            var http = new XsollaIdHttp();
            var discovery = new OidcDiscovery(config.Active.DiscoveryUrl, http);

            return new XsollaIdAuth(
                config,
                discovery,
                new XsollaIdWebBrowser(),
                new XsollaIdTokenClient(config, discovery, http),
                new PlayerPrefsXsollaIdSessionStore(prefs));
        }

        // Загрузчик картинок отдельно от входа: он не зависит ни от конфига, ни
        // от discovery, и нужен UI даже когда вход недоступен — тогда игровой
        // код возьмёт UnavailableXsollaIdAvatarLoader и покажет заглушку, снова
        // без единого #if.
        public static IXsollaIdAvatarLoader CreateAvatarLoader() => new XsollaIdAvatarLoader();
    }
}
