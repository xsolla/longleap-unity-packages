namespace LongLeap.XsollaId.UI
{
    // Что виджет аккаунта показывает прямо сейчас.
    //
    // Отдельное состояние, а не набор проверок по месту: у IXsollaIdAuth три
    // независимых флага (IsAvailable/IsReady/IsSignedIn) плюс две задачи в
    // полёте, и сводить их к картинке в двух местах — гарантированная
    // рассинхронизация.
    public enum AccountWidgetState
    {
        // Входа нет вовсе: блок спрятан целиком, id.xsolla.com не трогаем.
        Hidden,

        // Ответ ещё не окончательный — показывать гостем рано.
        Checking,

        // Кнопка входа.
        SignedOut,

        // Окно входа открыто.
        SigningIn,

        // Кнопка входа плюс строка с причиной прошлой неудачи.
        SignedOutWithError,

        // Аватар, имя и «выйти».
        SignedIn,

        // Выход в полёте: «выйти» не кликается.
        SigningOut
    }

    // Чистые решения виджета аккаунта.
    //
    // Ни одного обращения к UnityEngine — поэтому проверяется тестами в EditMode
    // без сцены, без префаба и без WebGL-сборки. Это единственная часть виджета,
    // где ошибка не видна глазами: SetActive на не том объекте заметен сразу, а
    // перепутанный приоритет состояний — нет.
    public static class XsollaIdAccountWidgetLogic
    {
        // Ключи локализации для причин неудачи. Держим здесь, а не в виджете:
        // сопоставление кода и ключа — часть решения, и оно проверяется тестом
        // на полноту перечисления.
        public const string ErrorKeyPopupBlocked = "xsolla_err_popup_blocked";
        public const string ErrorKeyNetwork = "xsolla_err_network";
        public const string ErrorKeyTimeout = "xsolla_err_timeout";
        public const string ErrorKeyFailed = "xsolla_err_failed";
        public const string ErrorKeyUnavailable = "xsolla_err_unavailable";

        // Порядок проверок здесь несущий — см. комментарии по ходу.
        public static AccountWidgetState Resolve(
            bool serviceExists,
            bool isAvailable,
            bool isReady,
            bool readyTimedOut,
            bool isSignedIn,
            bool signInPending,
            bool signOutPending,
            bool hasError)
        {
            // Недоступность — окончательный ответ, и он сильнее всего
            // остального, поэтому здесь не «погасить», а «спрятать».
            if (serviceExists && !isAvailable)
                return AccountWidgetState.Hidden;

            // Сервиса ещё нет: InitAccount в бутстрапе идёт ПОЗЖЕ, чем OnEnable
            // виджета. Гостем показывать нельзя — мы просто не знаем.
            if (!serviceExists)
                return AccountWidgetState.Checking;

            if (isSignedIn && signOutPending)
                return AccountWidgetState.SigningOut;

            // ВЫШЕ signInPending намеренно: SignedIn прилетает из Adopt раньше,
            // чем завершается задача SignInAsync. Наоборот — и строка мигнула бы
            // обратно на «входим…» уже после успешного входа.
            if (isSignedIn)
                return AccountWidgetState.SignedIn;

            // ВЫШЕ hasError: новая попытка бьёт прошлую ошибку, иначе игрок
            // жмёт кнопку и видит на ней старую жалобу.
            if (signInPending)
                return AccountWidgetState.SigningIn;

            if (!isReady && !readyTimedOut)
                return AccountWidgetState.Checking;

            if (hasError)
                return AccountWidgetState.SignedOutWithError;

            return AccountWidgetState.SignedOut;
        }

        // Видна ли раскрытая карточка аккаунта.
        //
        // Раскрытость — НЕ ещё одно состояние перечисления, а отдельный флаг:
        // она ортогональна остальным (раскрыть можно и в SigningOut, пока
        // «выйти» не кликается), и добавление её в Resolve размножило бы
        // состояния вдвое. Но и в виджете по месту это условие держать нельзя:
        // забыть свернуть карточку при выходе — ровно та ошибка, которую не
        // видно глазами, потому что вылезет она только на следующем входе.
        public static bool ShowsExpandedPanel(AccountWidgetState state, bool expanded) =>
            expanded && (state == AccountWidgetState.SignedIn || state == AccountWidgetState.SigningOut);

        // Обрезка по длине и ничего больше.
        //
        // Отдельно от ShortName: username это не адрес почты, и резать его по
        // '@' нечего — а вот длину ограничить надо, иначе сгенерированный
        // id<цифры> растянет карточку.
        public static string Shorten(string value, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return Truncate(value.Trim(), maxChars);
        }

        // Имя для строки виджета: локальная часть e-mail плюс обрезка по длине.
        //
        // Возвращает null, если показывать нечего — виджет подставит
        // локализованный запасной вариант. Пустую строку тоже приводим к null:
        // для UI это одно и то же, а вызывающему проще одна проверка.
        public static string ShortName(string displayName, int maxChars)
        {
            string local = DisplayNameFormat.LocalPart(displayName);

            if (string.IsNullOrWhiteSpace(local))
                return null;

            return Truncate(local, maxChars);
        }

        // Ключ локализации для игрока либо null, если говорить не о чем.
        public static string ErrorKey(XsollaIdErrorCode code)
        {
            switch (code)
            {
                // Игрок сам закрыл окно — это не ошибка, и жаловаться на
                // собственное решение игрока незачем.
                case XsollaIdErrorCode.Cancelled:

                // Штатный исход через 30 суток: молча становимся гостем.
                case XsollaIdErrorCode.SessionExpired:
                    return null;

                case XsollaIdErrorCode.PopupBlocked:
                    return ErrorKeyPopupBlocked;

                case XsollaIdErrorCode.Network:
                    return ErrorKeyNetwork;

                case XsollaIdErrorCode.Timeout:
                    return ErrorKeyTimeout;

                // Игроку эти два неотличимы: вход не состоялся, стоит повторить.
                // Разницу расскажет Failure.Message в консоли.
                case XsollaIdErrorCode.Unavailable:
                case XsollaIdErrorCode.NotConfigured:
                    return ErrorKeyUnavailable;

                // InvalidState, TokenExchangeFailed, PictureUnavailable и всё
                // новое, что появится в перечислении. Общая формулировка лучше
                // молчания: молчание игрок читает как «кнопка не работает».
                default:
                    return ErrorKeyFailed;
            }
        }

        private static string Truncate(string value, int maxChars)
        {
            // Обрезка выключена.
            if (maxChars <= 0 || value.Length <= maxChars)
                return value;

            int cut = maxChars - 1;

            // Не разрезаем суррогатную пару: половина эмодзи рисуется
            // «крокозяброй». Пара занимает два char'а, поэтому отступаем на один.
            if (cut > 0 && char.IsHighSurrogate(value[cut - 1]))
                cut--;

            return value.Substring(0, cut) + "…";
        }
    }
}
