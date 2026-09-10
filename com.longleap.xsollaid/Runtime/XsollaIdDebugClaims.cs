#if XSOLLA_ID_DEBUG
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LongLeap.XsollaId
{
    // Снимок клеймов последнего разобранного userinfo — исключительно для
    // отладочной панели.
    //
    // Файл целиком под XSOLLA_ID_DEBUG и в сборку на площадку не попадает: здесь
    // лежат e-mail и имя живого игрока, и держать их в памяти релиза незачем.
    // Ровно так же устроены XsollaIdDebugPanel и FakeXsollaIdAuth.
    //
    // Почему снимок, а не поле в XsollaIdSession: сессия сериализуется в
    // PlayerPrefs (PlayerPrefsXsollaIdSessionStore), так что клеймы поехали бы на
    // диск и поменяли формат блоба. Отладке это не нужно, а игроку тем более.
    //
    // Статика переживает перезагрузку сцены (а она случается при смерти игрока),
    // и здесь это как раз то, что нужно: userinfo дёргается один раз на личность,
    // а панель пересоздаётся. Взамен панель ОБЯЗАНА показывать снимок только у
    // вошедшего игрока — иначе после выхода на экране останутся чужие клеймы.
    public static class XsollaIdDebugClaims
    {
        // Те самые четыре, из которых XsollaIdTokenClient.PickDisplayName выбирает
        // имя, и в том же порядке: первый непустой и становится DisplayName.
        // Порядок здесь — не украшение, по нему на панели видно, какой клейм
        // победил и какие остались непрочитанными.
        public static readonly string[] Watched =
        {
            "username", "preferred_username", "name", "email"
        };

        private static readonly Dictionary<string, string> Values =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private static string[] _allKeys = Array.Empty<string>();

        // false — userinfo в этом запуске не случался. Это само по себе показание:
        // у восстановленной сессии с ProfileFetched = true запроса нет вовсе, и
        // имя приезжает из PlayerPrefs, а не с сервера.
        public static bool HasSnapshot { get; private set; }

        // Только ИМЕНА всех клеймов ответа, без значений: по ним видно, есть ли в
        // ответе вообще nickname или given_name, и при этом на экран не попадает
        // ничего, о чём не просили.
        public static IReadOnlyList<string> AllKeys => _allKeys;

        public static void Capture(JObject claims)
        {
            if (claims == null)
                return;

            Values.Clear();

            var keys = new List<string>();

            foreach (KeyValuePair<string, JToken> pair in claims)
            {
                keys.Add(pair.Key);

                // Нестроковый клейм показываем сырым JSON'ом: ReadStringClaim
                // отдаёт значение строго по типу, поэтому PickDisplayName такой
                // клейм пропустит — и на панели должно быть видно, что значение
                // есть, а имя из него всё равно не возьмётся.
                Values[pair.Key] = pair.Value is JValue value && value.Type == JTokenType.String
                    ? (string)value
                    : pair.Value?.ToString(Formatting.None);
            }

            keys.Sort(StringComparer.Ordinal);

            _allKeys = keys.ToArray();
            HasSnapshot = true;
        }

        // Три разных ответа намеренно различимы: "?" — userinfo не спрашивали,
        // "-" — спрашивали, но клейма в ответе нет (или он пустой), всё остальное
        // — значение. Пустая строка на панели читалась бы как «не знаю».
        public static string Value(string key)
        {
            if (!HasSnapshot)
                return "?";

            return Values.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value)
                ? value
                : "-";
        }
    }
}
#endif
