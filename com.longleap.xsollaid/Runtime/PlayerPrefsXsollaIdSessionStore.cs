using System;
using LongLeap.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LongLeap.XsollaId
{
    // Сессия Xsolla ID в PlayerPrefs (в WebGL это IndexedDB).
    //
    // КЛЮЧ СВОЙ И ЧУЖИЕ НЕ ТРОГАЕТ. Анонимная личность Firebase лежит в
    // "longleap_auth", и её надо сохранить нетронутой: именно она держит
    // облачные сохранения, и именно благодаря ей выход из аккаунта возвращает
    // игрока к ТОЙ ЖЕ гостевой личности, а не заводит новую с пустым
    // прогрессом. На это есть отдельный тест.
    public sealed class PlayerPrefsXsollaIdSessionStore : IXsollaIdSessionStore
    {
        private const string StoreKey = "longleap_xsollaid";

        private readonly IPlayerPrefs _prefs;

        public PlayerPrefsXsollaIdSessionStore(IPlayerPrefs prefs)
        {
            _prefs = prefs ?? throw new ArgumentNullException(nameof(prefs));
        }

        public XsollaIdSession Load()
        {
            string json = _prefs.GetString(StoreKey, null);

            if (string.IsNullOrEmpty(json))
                return null;

            try
            {
                JObject o = JObject.Parse(json);

                string subject = ReadString(o, "subject");
                string access = ReadString(o, "access");
                string refresh = ReadString(o, "refresh");

                // Без sub и без refresh восстанавливать нечего: тихо считаем,
                // что сессии нет.
                if (string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(refresh))
                {
                    Clear();
                    return null;
                }

                return new XsollaIdSession(
                    new XsollaUserId(subject),
                    ReadString(o, "displayName"),
                    string.IsNullOrEmpty(access) ? AccessToken.None : new AccessToken(access),
                    new RefreshToken(refresh),
                    ReadLong(o, "expiresAtUnixMs"),

                    // Адрес перепроверяем на чтении, а не доверяем блобу: его
                    // мог записать билд с другими правилами.
                    AvatarUrl.TryParse(ReadString(o, "picture")),

                    // Ключа нет — блоб записан до появления картинки. Считаем
                    // профиль непрочитанным: XsollaIdAuth дотянет его один раз
                    // и перезапишет блоб уже в новом виде.
                    ReadBool(o, "profileFetched"),

                    // У блоба, записанного до появления этих двух ключей, они
                    // приедут null'ами. Отдельного признака миграции им не надо:
                    // profileFetched выше уже решает, идти ли за userinfo, а
                    // раскрытая карточка на null отвечает прочерком.
                    ReadString(o, "userName"),
                    ReadString(o, "fullName"));
            }
            catch (Exception ex)
            {
                // Битый блоб — не повод падать на старте. Выкидываем и живём
                // дальше гостем.
                Debug.LogWarning(
                    $"{nameof(PlayerPrefsXsollaIdSessionStore)}: failed to read the session, key cleared. {ex.Message}");
                Clear();
                return null;
            }
        }

        public void Save(XsollaIdSession session)
        {
            if (session == null)
            {
                Clear();
                return;
            }

            var o = new JObject
            {
                ["subject"] = session.Subject.Value,
                ["displayName"] = session.DisplayName,
                ["access"] = session.Access.Value,
                ["refresh"] = session.Refresh.Value,
                ["expiresAtUnixMs"] = session.ExpiresAtUnixMs,

                // Эти два ключа пишем ВСЕГДА, даже когда значений нет.
                // Отсутствие ключа — это признак блоба, записанного до
                // появления картинки, и именно по нему Load решает, что профиль
                // надо перечитать. Если писать ключ только при непустом
                // значении, признак сломается и миграция не случится.
                //
                // Пустой адрес уедет как "picture": null — ключ на месте, а это
                // всё, что нужно. displayName выше ведёт себя так же.
                ["picture"] = session.Picture.Value,
                ["profileFetched"] = session.ProfileFetched,

                // Тоже всегда, тоже вместе с null'ами — по той же причине, что
                // picture выше.
                ["userName"] = session.UserName,
                ["fullName"] = session.FullName
            };

            _prefs.SetString(StoreKey, o.ToString(Newtonsoft.Json.Formatting.None));
            _prefs.Save();
        }

        public void Clear()
        {
            _prefs.DeleteKey(StoreKey);
            _prefs.Save();
        }

        // Поля блоба читаем строго по типу, а не через (string)o[key].
        //
        // Приведение поля, которое приехало объектом или массивом, бросает
        // InvalidCastException — и его ловит catch в Load, который ЧИСТИТ КЛЮЧ.
        // То есть одно мусорное поле стоило бы игроку всей сессии. Блоб мог
        // записать билд с другими правилами, а в WebGL это IndexedDB, куда
        // игрок способен залезть руками.
        private static string ReadString(JObject o, string key) =>
            o[key] is JValue value && value.Type == JTokenType.String ? (string)value : null;

        private static bool ReadBool(JObject o, string key) =>
            o[key] is JValue value && value.Type == JTokenType.Boolean && (bool)value;

        private static long ReadLong(JObject o, string key) =>
            o[key] is JValue value && (value.Type == JTokenType.Integer || value.Type == JTokenType.Float)
                ? (long)value
                : 0L;
    }
}
