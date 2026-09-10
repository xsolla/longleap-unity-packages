namespace LongLeap.XsollaId
{
    // Снимок сессии Xsolla ID: кто вошёл и чем это подтверждается.
    //
    // Неизменяемый. Обновление токена не мутирует объект, а создаёт новый —
    // так подписчики события SignedIn не могут увидеть полуобновлённое
    // состояние, а сравнение «сессия та же?» сводится к сравнению ссылок.
    public sealed class XsollaIdSession
    {
        // sub из токена. Единственное, что можно считать устойчивым ключом
        // пользователя — но НЕ Firebase uid, см. XsollaUserId.
        public XsollaUserId Subject { get; }

        // Имя для UI: username или email из userinfo. Может быть null — если
        // запрошен только scope openid, показывать будет нечего.
        public string DisplayName { get; }

        // Адрес картинки профиля. None — штатно, см. UserInfoResponse.Picture.
        public AvatarUrl Picture { get; }

        // Сырые клеймы username и name — их показывает раскрытая карточка
        // аккаунта, где под каждую величину есть своя строка. Почему этого не
        // хватает одного DisplayName — см. UserInfoResponse.UserName.
        //
        // Оба могут быть null, и это штатно: у аккаунта без имени в профиле
        // клейма name не будет вовсе.
        public string UserName { get; }
        public string FullName { get; }

        // Спрашивали ли уже userinfo для ЭТОЙ личности.
        //
        // Признак нужен отдельно от «имя непустое», которое служило им раньше.
        // У картинки клейма может не быть вовсе, и условие «картинки нет →
        // сходи ещё раз» дёргало бы userinfo на каждое обновление токена —
        // навсегда. Признак сохраняется вместе с сессией, поэтому догоняющий
        // запрос случается ровно один раз на игрока.
        public bool ProfileFetched { get; }

        public AccessToken Access { get; }

        public RefreshToken Refresh { get; }

        // Момент истечения access token'а, unix-время в миллисекундах.
        public long ExpiresAtUnixMs { get; }

        // picture и profileFetched — ТОЛЬКО в конце и только со значениями по
        // умолчанию. Вставить picture рядом с displayName, где ему место по
        // смыслу, нельзя: пять мест передают аргументы позиционно, и
        // перестановка молча привязала бы их не к тем параметрам.
        //
        // profileFetched по умолчанию false, потому что безопасный ответ на
        // вопрос «спрашивали ли?» — «нет»: путь, который забыл его выставить,
        // получит один лишний запрос, а не навсегда потерянную картинку.
        public XsollaIdSession(
            XsollaUserId subject,
            string displayName,
            AccessToken access,
            RefreshToken refresh,
            long expiresAtUnixMs,
            AvatarUrl picture = default,
            bool profileFetched = false,
            string userName = null,
            string fullName = null)
        {
            Subject = subject;
            DisplayName = displayName;
            Access = access;
            Refresh = refresh;
            ExpiresAtUnixMs = expiresAtUnixMs;
            Picture = picture;
            ProfileFetched = profileFetched;
            UserName = userName;
            FullName = fullName;
        }

        // Имя для показа игроку: если DisplayName это адрес почты, домен
        // отрезан. Сырой клейм остаётся в DisplayName — он нужен в логах и
        // отладочной панели. См. DisplayNameFormat.
        public string ShortDisplayName => DisplayNameFormat.LocalPart(DisplayName);

        // Есть чем обновиться без участия игрока.
        public bool CanRefresh => Refresh.IsValid;

        // Истёк ли access token с запасом marginMs.
        //
        // Запас нужен, чтобы не отправить запрос с токеном, который протухнет
        // по дороге. Значение запаса задаёт вызывающий — здесь нет константы
        // намеренно: политику обновления держит XsollaIdAuth, а не снимок.
        public bool IsExpired(long nowUnixMs, long marginMs) =>
            !Access.IsValid || nowUnixMs >= ExpiresAtUnixMs - marginMs;

        // Токены наружу не печатаем: их прячут сами типы, но напомним и здесь.
        public override string ToString() =>
            $"XsollaIdSession({Subject}, displayName={(string.IsNullOrEmpty(DisplayName) ? "none" : "set")}, " +
            $"userName={(string.IsNullOrEmpty(UserName) ? "none" : "set")}, " +
            $"fullName={(string.IsNullOrEmpty(FullName) ? "none" : "set")}, " +
            $"picture={(Picture.IsValid ? "set" : "none")}, profileFetched={ProfileFetched}, " +
            $"expiresAt={ExpiresAtUnixMs}, canRefresh={CanRefresh})";
    }
}
