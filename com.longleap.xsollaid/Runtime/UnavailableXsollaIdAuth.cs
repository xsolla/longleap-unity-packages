using System;
using System.Threading.Tasks;

namespace LongLeap.XsollaId
{
    // Заглушка на случай, когда входа нет: портальная сборка, выключенный
    // дефайн XSOLLA_ID или незаполненный конфиг.
    //
    // Существует, чтобы в игровом коде не было ни одного #if: UI спрашивает
    // IsAvailable и прячет блок аккаунта (именно прячет, а не гасит — как
    // HideQuitButtonOnPortal и как кнопка воскрешения в GameOverScreen, когда
    // рекламы на площадке нет вовсе).
    //
    // На GamePix это не «на всякий случай», а требование: T&C (3.3) запрещают
    // обращаться к любому адресу, кроме площадки, и проверяют это инспекцией
    // трафика. Xsolla ID ходит на id.xsolla.com, поэтому там его быть не
    // должно ни одним запросом.
    public sealed class UnavailableXsollaIdAuth : IXsollaIdAuth
    {
        public bool IsAvailable => false;

        // Ждать нечего: ответ окончательный с первого кадра.
        public bool IsReady => true;

        public bool IsSignedIn => false;

        // Входа тут не бывает вовсе, поэтому и жеста игрока быть не может.
        public XsollaIdSignInOrigin LastSignInOrigin => XsollaIdSignInOrigin.Restored;

        public XsollaIdSession Current => null;

        // Никогда не срабатывают. Объявлены, чтобы подписка в UI не требовала
        // проверок; add/remove пустые, поэтому и утечь тут нечему.
        public event Action<XsollaIdSession> SignedIn { add { } remove { } }
        public event Action SignedOut { add { } remove { } }
        public event Action<XsollaIdSession> SessionUpdated { add { } remove { } }

        public Task<XsollaIdResult> SignInAsync() => Task.FromResult(XsollaIdResult.Unavailable());

        public Task<XsollaIdResult> RestoreAsync() => Task.FromResult(XsollaIdResult.Unavailable());

        public Task<XsollaIdResult> RefreshAsync() => Task.FromResult(XsollaIdResult.Unavailable());

        public Task SignOutAsync() => Task.CompletedTask;
    }
}
