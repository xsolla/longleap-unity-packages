using System;
using NUnit.Framework;

namespace LongLeap.XsollaId.UI.Tests
{
    [TestFixture]
    public class XsollaIdAccountWidgetLogicTests
    {
        // Named arguments are mandatory: eight bools in a row are readable
        // only this way.
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

        // Unavailability overrides EVERYTHING else, in any combination of the other flags.
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

        // The game's bootstrap may bind the account service later than the
        // widget's OnEnable, so for the first frames there may be no service at
        // all. The sign-in button must not flash then: a player who signed in last
        // time would see "Sign in" and think they were signed out.
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

        // The player should not see "checking" forever: restore goes to the network.
        [Test]
        public void ReadyTimedOut_FallsBackToSignedOut()
        {
            Assert.AreEqual(AccountWidgetState.SignedOut, Resolve(isReady: false, readyTimedOut: true));
        }

        // ---- sign-in ----

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

        // Regression: XsollaIdAuth raises SignedIn before the SignInAsync task
        // completes, so both flags are true for a while. If signInPending won,
        // the label would flash back to "Signing in..." after a successful sign-in.
        [Test]
        public void SignedInWhileTheTaskIsStillRunning_ShowsSignedIn()
        {
            Assert.AreEqual(AccountWidgetState.SignedIn, Resolve(isSignedIn: true, signInPending: true));
        }

        // The player pressed "Sign in" again: the previous error must not stay
        // on the button.
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

        // ---- sign-out ----

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

        // How the domain is cut is DisplayNameFormat.LocalPart's job and is tested
        // there; here only that ShortName uses it, and that having nothing to show
        // is null, so the widget substitutes a fallback.
        [TestCase("user@mail.com", ExpectedResult = "user")]
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

        // Truncation must not split a surrogate pair: half an emoji renders as
        // garbage. "ab" + 😀: the emoji takes two chars, and the 4-character
        // limit falls right on it.
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

        // Covers every code, including ones added later: a new code either gets a
        // key or is silent on purpose — never an empty space where the reason
        // should be. Cancelled (the player closed the window) and SessionExpired
        // are not errors.
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

        // ---- expanded card ----

        [Test]
        public void ShowsExpandedPanel_CollapsedFlagAlwaysWins()
        {
            foreach (AccountWidgetState state in (AccountWidgetState[])Enum.GetValues(typeof(AccountWidgetState)))
            {
                Assert.IsFalse(
                    XsollaIdAccountWidgetLogic.ShowsExpandedPanel(state, expanded: false),
                    $"{state} with the collapsed flag must not show the card");
            }
        }

        // Covers every state: the card with the player's name and username shows
        // only while signed in — including SigningOut, so the card does not
        // flicker while the token is being revoked. Forgetting to collapse it on
        // sign-out is a bug visible only on the next sign-in.
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
            // Exactly why ShortName does not fit here: '@' is not a separator.
            Assert.AreEqual("id12345678", XsollaIdAccountWidgetLogic.Shorten("id12345678", 18));
            Assert.AreEqual("a@b.c", XsollaIdAccountWidgetLogic.Shorten("a@b.c", 18));
        }

        [Test]
        public void Shorten_TruncatesTooLong_AndNullsOutEmpty()
        {
            Assert.AreEqual("id1234567…", XsollaIdAccountWidgetLogic.Shorten("id1234567890123", 10));
            Assert.IsNull(XsollaIdAccountWidgetLogic.Shorten(null, 18));
            Assert.IsNull(XsollaIdAccountWidgetLogic.Shorten("   ", 18));
        }
    }
}
