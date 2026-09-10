namespace LongLeap.XsollaId
{
    // Итог операции входа. Закрытое объединение той же формы, что и
    // LongLeap.Core.Auth.AuthResult: приватный конструктор + вложенные sealed
    // наследники, так что снаружи разбирают через `result is
    // XsollaIdResult.Success success`.
    //
    // Наружу из IXsollaIdAuth исключения не летят НИКОГДА — как и в
    // FirebaseAnonymousAuthProvider. Любой сбой приезжает сюда как Failure:
    // исключение, всплывшее в Task, который опрашивают из корутины, — это
    // повисший бутстрап.
    public abstract class XsollaIdResult
    {
        private XsollaIdResult() { }

        public sealed class Success : XsollaIdResult
        {
            public XsollaIdSession Session { get; }

            public Success(XsollaIdSession session) => Session = session;
        }

        public sealed class Failure : XsollaIdResult
        {
            public XsollaIdErrorCode Code { get; }

            // Текст для лога, не для игрока: игроку показываем
            // локализованную строку, выбранную по Code. Токенов не содержит.
            public string Message { get; }

            public Failure(XsollaIdErrorCode code, string message)
            {
                Code = code;
                Message = message;
            }
        }

        // Короткие фабрики для самых частых исходов — чтобы в коде не плодить
        // одинаковые тексты.
        public static XsollaIdResult Cancelled() =>
            new Failure(XsollaIdErrorCode.Cancelled, "The player closed the sign-in window.");

        public static XsollaIdResult Unavailable() =>
            new Failure(XsollaIdErrorCode.Unavailable, "Xsolla ID sign-in is not available in this build.");

        public static XsollaIdResult Expired() =>
            new Failure(XsollaIdErrorCode.SessionExpired, "There is no saved session, or it is no longer valid.");
    }
}
