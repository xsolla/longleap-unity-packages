#if UNITY_EDITOR
using System;
using System.Threading.Tasks;

namespace LongLeap.XsollaId.UI
{
    // Поддельный сервис входа — ТОЛЬКО для редактора.
    //
    // Настоящий вход в редакторе недоступен всегда: XsollaIdWebBrowser.IsSupported
    // живёт под #if UNITY_WEBGL && !UNITY_EDITOR, поэтому фабрика отдаёт
    // UnavailableXsollaIdAuth, и виджет честно прячется. Верстку «вошёл» без
    // подделки не увидеть, не собрав WebGL и не подняв serve.py.
    //
    // Файл целиком под #if UNITY_EDITOR и ни на одно сериализованное поле не
    // ссылается — в сборку не попадает ни байтом. Сериализованных ссылок на него
    // нет намеренно: иначе в билде получили бы Missing Script.
    public sealed class FakeXsollaIdAuth : IXsollaIdAuth
    {
        private FakeXsollaIdAuth(bool available, bool ready, XsollaIdSession session)
        {
            IsAvailable = available;
            IsReady = ready;
            Current = session;
        }

        public bool IsAvailable { get; }
        public bool IsReady { get; }
        public bool IsSignedIn => Current != null;

        // Подделка нужна превью и тестам вёрстки, а не переключению сейвов,
        // поэтому признак неизменяемый и заведомо не интерактивный.
        public XsollaIdSignInOrigin LastSignInOrigin => XsollaIdSignInOrigin.Restored;
        public XsollaIdSession Current { get; private set; }

        // Никогда не срабатывают: состояние задаётся при создании.
        public event Action<XsollaIdSession> SignedIn { add { } remove { } }
        public event Action SignedOut { add { } remove { } }
        public event Action<XsollaIdSession> SessionUpdated { add { } remove { } }

        // Имена фабрик НЕ повторяют имена событий: SignedIn и SignedOut уже
        // заняты членами интерфейса.
        public static IXsollaIdAuth Guest() => new FakeXsollaIdAuth(true, true, null);

        public static IXsollaIdAuth Restoring() => new FakeXsollaIdAuth(true, false, null);

        public static IXsollaIdAuth Unavailable() => new FakeXsollaIdAuth(false, true, null);

        // userName и fullName со значениями по умолчанию: раскрытая карточка
        // показывает их по отдельности, и превью без них проверяло бы только
        // запасной путь (когда клеймов нет), а не обычный.
        public static IXsollaIdAuth SignedInAs(
            string displayName, string userName = null, string fullName = null) => new FakeXsollaIdAuth(
            true, true,
            new XsollaIdSession(
                new XsollaUserId("preview-sub"), displayName,
                new AccessToken("preview-access"), new RefreshToken("preview-refresh"),
                expiresAtUnixMs: long.MaxValue,
                picture: AvatarUrl.None,
                profileFetched: true,
                userName: userName,
                fullName: fullName));

        // Окон не открывает и в сеть не ходит.
        public Task<XsollaIdResult> SignInAsync() =>
            Task.FromResult(IsSignedIn
                ? new XsollaIdResult.Success(Current)
                : XsollaIdResult.Cancelled());

        public Task<XsollaIdResult> RestoreAsync() =>
            Task.FromResult(IsSignedIn
                ? new XsollaIdResult.Success(Current)
                : XsollaIdResult.Expired());

        public Task<XsollaIdResult> RefreshAsync() => RestoreAsync();

        public Task SignOutAsync()
        {
            Current = null;
            return Task.CompletedTask;
        }
    }
}
#endif
