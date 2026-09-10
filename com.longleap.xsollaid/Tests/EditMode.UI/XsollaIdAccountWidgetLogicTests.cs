using System;
using NUnit.Framework;

namespace LongLeap.XsollaId.UI.Tests
{
    [TestFixture]
    public class XsollaIdAccountWidgetLogicTests
    {
        // Именованные аргументы обязательны: восемь bool'ов подряд читаются
        // только так.
        private static AccountWidgetState Resolve(
            bool serviceExists = true,
            bool isAvailable = true,
            bool isReady = true,
            bool readyTimedOut = false,
            bool isSignedIn = false,
            bool signInPending = false,
            bool signOutPending = false,
            bool hasError = false) =>
            XsollaIdAccountWidgetLogic.Resolve(
                serviceExists, isAvailable, isReady, readyTimedOut,
                isSignedIn, signInPending, signOutPending, hasError);

        // ---- Hidden ----

        // Недоступность бьёт ВСЁ остальное, в любой комбинации прочих флагов.
        // На GamePix это не вкусовщина: T&C (3.3) запрещают обращаться к любому
        // адресу, кроме площадки, и проверяют инспекцией трафика. Отрисованная
        // кнопка входа там — нарушение.
        [Test]
        public void Unavailable_IsHidden_WhateverElseIsTrue(
            [Values(true, false)] bool isReady,
            [Values(true, false)] bool readyTimedOut,
            [Values(true, false)] bool isSignedIn,
            [Values(true, false)] bool signInPending,
            [Values(true, false)] bool signOutPending,
            [Values(true, false)] bool hasError)
        {
            AccountWidgetState state = Resolve(
                isAvailable: false,
                isReady: isReady,
                readyTimedOut: readyTimedOut,
                isSignedIn: isSignedIn,
                signInPending: signInPending,
                signOutPending: signOutPending,
                hasError: hasError);

            Assert.AreEqual(AccountWidgetState.Hidden, state);
        }

        // ---- Checking ----

        // GameBootstrapper.InitAccount идёт позже, чем OnEnable виджета, поэтому
        // на первых кадрах сервиса может не быть вовсе. Мигать кнопкой входа в
        // этот момент нельзя: игрок, который вошёл в прошлый раз, увидел бы
        // «войти» и решил, что его выкинуло.
        [Test]
        public void NoServiceYet_IsChecking()
        {
            Assert.AreEqual(AccountWidgetState.Checking, Resolve(serviceExists: false, isAvailable: false));
            Assert.AreEqual(AccountWidgetState.Checking, Resolve(serviceExists: false));
        }

        [Test]
        public void NotReadyYet_IsChecking()
        {
            Assert.AreEqual(AccountWidgetState.Checking, Resolve(isReady: false));
        }

        // Вечное «проверяем» игрок не заслужил: восстановление ходит в сеть.
        [Test]
        public void ReadyTimedOut_FallsBackToSignedOut()
        {
            Assert.AreEqual(AccountWidgetState.SignedOut, Resolve(isReady: false, readyTimedOut: true));
        }

        // ---- вход ----

        [Test]
        public void ReadyAndSignedOut_ShowsTheButton()
        {
            Assert.AreEqual(AccountWidgetState.SignedOut, Resolve());
        }

        [Test]
        public void SignInPending_IsSigningIn()
        {
            Assert.AreEqual(AccountWidgetState.SigningIn, Resolve(signInPending: true));
        }

        // РЕГРЕССИЯ. Adopt в XsollaIdAuth поднимает SignedIn РАНЬШЕ, чем
        // завершится задача SignInAsync, поэтому оба флага какое-то время
        // истинны. Если signInPending окажется приоритетнее, строка после
        // успешного входа мигнёт обратно на «входим…».
        [Test]
        public void SignedInWhileTheTaskIsStillRunning_ShowsSignedIn()
        {
            Assert.AreEqual(AccountWidgetState.SignedIn, Resolve(isSignedIn: true, signInPending: true));
        }

        // Игрок нажал «войти» ещё раз — на кнопке не должна висеть прошлая
        // жалоба.
        [Test]
        public void NewAttempt_OutranksAStaleError()
        {
            Assert.AreEqual(AccountWidgetState.SigningIn, Resolve(signInPending: true, hasError: true));
        }

        [Test]
        public void ErrorWithNothingPending_ShowsTheErrorLine()
        {
            Assert.AreEqual(AccountWidgetState.SignedOutWithError, Resolve(hasError: true));
        }

        // ---- выход ----

        [Test]
        public void SignedIn_ShowsTheRow()
        {
            Assert.AreEqual(AccountWidgetState.SignedIn, Resolve(isSignedIn: true));
        }

        [Test]
        public void SignOutPending_IsSigningOut()
        {
            Assert.AreEqual(AccountWidgetState.SigningOut, Resolve(isSignedIn: true, signOutPending: true));
        }

        [Test]
        public void SignedIn_OutranksALeftoverError()
        {
            Assert.AreEqual(
                AccountWidgetState.SigningOut,
                Resolve(isSignedIn: true, signOutPending: true, hasError: true));

            Assert.AreEqual(AccountWidgetState.SignedIn, Resolve(isSignedIn: true, hasError: true));
        }

        // ---- ShortName ----

        [TestCase("anton@mail.com", ExpectedResult = "anton")]
        [TestCase("anton", ExpectedResult = "anton")]
        [TestCase("anton@mail.com@x", ExpectedResult = "anton")]
        [TestCase("Антон@почта.рф", ExpectedResult = "Антон")]
        // Пустая локальная часть — это не адрес, а мусор, и он доезжает до
        // экрана как есть: странное имя лучше пустого места. Решение живёт в
        // DisplayNameFormat.LocalPart и проверено там же.
        [TestCase("@mail.com", ExpectedResult = "@mail.com")]
        // А вот показывать НЕЧЕГО — это null: виджет подставит запасной вариант.
        [TestCase(null, ExpectedResult = null)]
        [TestCase("", ExpectedResult = null)]
        [TestCase("   ", ExpectedResult = null)]
        public string ShortName_StripsTheDomain(string displayName) =>
            XsollaIdAccountWidgetLogic.ShortName(displayName, 18);

        [Test]
        public void ShortName_TruncatesWithAnEllipsis()
        {
            Assert.AreEqual("averyve…", XsollaIdAccountWidgetLogic.ShortName("averyveryverylongname", 8));
        }

        [Test]
        public void ShortName_ExactlyAtTheLimit_IsNotTruncated()
        {
            Assert.AreEqual("exact8ch", XsollaIdAccountWidgetLogic.ShortName("exact8ch", 8));
        }

        [Test]
        public void ShortName_NonPositiveLimit_DisablesTruncation()
        {
            Assert.AreEqual("averyveryverylongname", XsollaIdAccountWidgetLogic.ShortName("averyveryverylongname", 0));
            Assert.AreEqual("averyveryverylongname", XsollaIdAccountWidgetLogic.ShortName("averyveryverylongname", -1));
        }

        // Обрезка не должна разрубить суррогатную пару: половина эмодзи
        // рисуется «крокозяброй». "ab" + 😀 — эмодзи занимает два char'а, и
        // граница в 4 символа падает ровно на него.
        [Test]
        public void ShortName_DoesNotSplitASurrogatePair()
        {
            string result = XsollaIdAccountWidgetLogic.ShortName("ab\U0001F600cd", 4);

            Assert.IsFalse(char.IsHighSurrogate(result[result.Length - 2]),
                "the ellipsis must not follow a dangling high surrogate");
            Assert.AreEqual("ab…", result);
        }

        // ---- ErrorKey ----

        [Test]
        public void ErrorKey_SilentCodesSayNothing()
        {
            // Игрок сам закрыл окно, и сессия, истёкшая через 30 суток, — не
            // поводы для жалобы на экране.
            Assert.IsNull(XsollaIdAccountWidgetLogic.ErrorKey(XsollaIdErrorCode.Cancelled));
            Assert.IsNull(XsollaIdAccountWidgetLogic.ErrorKey(XsollaIdErrorCode.SessionExpired));
        }

        [Test]
        public void ErrorKey_MapsTheTalkativeCodes()
        {
            Assert.AreEqual(
                XsollaIdAccountWidgetLogic.ErrorKeyPopupBlocked,
                XsollaIdAccountWidgetLogic.ErrorKey(XsollaIdErrorCode.PopupBlocked));

            Assert.AreEqual(
                XsollaIdAccountWidgetLogic.ErrorKeyNetwork,
                XsollaIdAccountWidgetLogic.ErrorKey(XsollaIdErrorCode.Network));

            Assert.AreEqual(
                XsollaIdAccountWidgetLogic.ErrorKeyTimeout,
                XsollaIdAccountWidgetLogic.ErrorKey(XsollaIdErrorCode.Timeout));

            Assert.AreEqual(
                XsollaIdAccountWidgetLogic.ErrorKeyUnavailable,
                XsollaIdAccountWidgetLogic.ErrorKey(XsollaIdErrorCode.NotConfigured));

            Assert.AreEqual(
                XsollaIdAccountWidgetLogic.ErrorKeyFailed,
                XsollaIdAccountWidgetLogic.ErrorKey(XsollaIdErrorCode.TokenExchangeFailed));
        }

        // ---- раскрытая карточка ----

        // Раскрытость ортогональна состоянию: SigningOut её не складывает, иначе
        // карточка мигала бы, пока отзыв токена в полёте.
        [Test]
        public void ShowsExpandedPanel_OnlyWhileSignedIn()
        {
            Assert.IsTrue(
                XsollaIdAccountWidgetLogic.ShowsExpandedPanel(AccountWidgetState.SignedIn, expanded: true));

            Assert.IsTrue(
                XsollaIdAccountWidgetLogic.ShowsExpandedPanel(AccountWidgetState.SigningOut, expanded: true),
                "выход в полёте не должен складывать карточку");
        }

        [Test]
        public void ShowsExpandedPanel_CollapsedFlagAlwaysWins()
        {
            foreach (AccountWidgetState state in (AccountWidgetState[])Enum.GetValues(typeof(AccountWidgetState)))
            {
                Assert.IsFalse(
                    XsollaIdAccountWidgetLogic.ShowsExpandedPanel(state, expanded: false),
                    $"{state} со свёрнутым флагом не должен показывать карточку");
            }
        }

        // Тест на ПОЛНОТУ: карточка с чужими именем и username не должна
        // показываться ни в одном состоянии, кроме двух «вошёл». Забыть свернуть
        // её при выходе — ошибка, которую видно только на следующем входе.
        [Test]
        public void ShowsExpandedPanel_HiddenInEveryStateButSignedIn()
        {
            foreach (AccountWidgetState state in (AccountWidgetState[])Enum.GetValues(typeof(AccountWidgetState)))
            {
                bool allowed =
                    state == AccountWidgetState.SignedIn || state == AccountWidgetState.SigningOut;

                Assert.AreEqual(
                    allowed,
                    XsollaIdAccountWidgetLogic.ShowsExpandedPanel(state, expanded: true),
                    $"{state}");
            }
        }

        // ---- Shorten ----

        [Test]
        public void Shorten_KeepsUsernameWhole_AndDoesNotCutAtAtSign()
        {
            // Ровно то, чем ShortName здесь не годится: '@' не разделитель.
            Assert.AreEqual("id19334455", XsollaIdAccountWidgetLogic.Shorten("id19334455", 18));
            Assert.AreEqual("a@b.c", XsollaIdAccountWidgetLogic.Shorten("a@b.c", 18));
        }

        [Test]
        public void Shorten_TruncatesTooLong_AndNullsOutEmpty()
        {
            Assert.AreEqual("id1234567…", XsollaIdAccountWidgetLogic.Shorten("id1234567890123", 10));
            Assert.IsNull(XsollaIdAccountWidgetLogic.Shorten(null, 18));
            Assert.IsNull(XsollaIdAccountWidgetLogic.Shorten("   ", 18));
        }

        // Тест на ПОЛНОТУ. Когда в пакете появится новый код ошибки, этот тест
        // либо промолчит (если код осмысленно попал в default), либо покажет, что
        // его забыли — вместо того чтобы игрок увидел пустое место там, где
        // должна быть причина.
        [Test]
        public void ErrorKey_EveryCodeIsEitherSilentOnPurposeOrHasAKey()
        {
            foreach (XsollaIdErrorCode code in (XsollaIdErrorCode[])Enum.GetValues(typeof(XsollaIdErrorCode)))
            {
                string key = XsollaIdAccountWidgetLogic.ErrorKey(code);

                bool silentOnPurpose =
                    code == XsollaIdErrorCode.Cancelled || code == XsollaIdErrorCode.SessionExpired;

                if (silentOnPurpose)
                {
                    Assert.IsNull(key, $"{code} must stay silent");
                    continue;
                }

                Assert.IsFalse(string.IsNullOrEmpty(key), $"{code} has no localization key");
            }
        }
    }
}
