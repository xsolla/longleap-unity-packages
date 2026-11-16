using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace LongLeap.XsollaId.UI.Tests
{
    [TestFixture]
    public class XsollaIdAccountWidgetLogicTests
    {
        // ---- Resolve ----

        // Flags are given as words: the defaults are "service exists, available,
        // ready, nothing else", and each word flips one of them.
        //   noService, unavailable, notReady  — turn a default off
        //   timedOut, signedIn, signInPending, signOutPending, error — turn a flag on
        private static AccountWidgetState Resolve(string flags)
        {
            var set = new HashSet<string>(
                (flags ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

            var known = new[]
            {
                "noService", "unavailable", "notReady", "timedOut",
                "signedIn", "signInPending", "signOutPending", "error"
            };

            foreach (string flag in set)
                Assert.Contains(flag, known, "typo in the test case");

            return XsollaIdAccountWidgetLogic.Resolve(
                serviceExists: !set.Contains("noService"),
                isAvailable: !set.Contains("unavailable"),
                isReady: !set.Contains("notReady"),
                readyTimedOut: set.Contains("timedOut"),
                isSignedIn: set.Contains("signedIn"),
                signInPending: set.Contains("signInPending"),
                signOutPending: set.Contains("signOutPending"),
                hasError: set.Contains("error"));
        }

        // No service yet: the game may bind it after the widget's OnEnable. The
        // sign-in button must not flash — a signed-in player would think they
        // were signed out. A missing service wins even over "unavailable".
        [TestCase("noService unavailable", ExpectedResult = AccountWidgetState.Checking)]
        [TestCase("noService", ExpectedResult = AccountWidgetState.Checking)]
        [TestCase("noService signedIn", ExpectedResult = AccountWidgetState.Checking)]
        // Restore still running.
        [TestCase("notReady", ExpectedResult = AccountWidgetState.Checking)]
        [TestCase("notReady error", ExpectedResult = AccountWidgetState.Checking)]
        // Not "checking" forever: restore goes to the network.
        [TestCase("notReady timedOut", ExpectedResult = AccountWidgetState.SignedOut)]
        [TestCase("", ExpectedResult = AccountWidgetState.SignedOut)]
        [TestCase("signOutPending", ExpectedResult = AccountWidgetState.SignedOut)]
        [TestCase("signInPending", ExpectedResult = AccountWidgetState.SigningIn)]
        [TestCase("notReady signInPending", ExpectedResult = AccountWidgetState.SigningIn)]
        // A new attempt hides the stale error.
        [TestCase("signInPending error", ExpectedResult = AccountWidgetState.SigningIn)]
        [TestCase("error", ExpectedResult = AccountWidgetState.SignedOutWithError)]
        [TestCase("signedIn", ExpectedResult = AccountWidgetState.SignedIn)]
        // Regression: SignedIn fires before the SignInAsync task completes; the
        // label must not flash back to "Signing in...".
        [TestCase("signedIn signInPending", ExpectedResult = AccountWidgetState.SignedIn)]
        [TestCase("signedIn notReady", ExpectedResult = AccountWidgetState.SignedIn)]
        // A leftover error never shows next to an account.
        [TestCase("signedIn error", ExpectedResult = AccountWidgetState.SignedIn)]
        [TestCase("signedIn signOutPending", ExpectedResult = AccountWidgetState.SigningOut)]
        [TestCase("signedIn signOutPending error", ExpectedResult = AccountWidgetState.SigningOut)]
        public AccountWidgetState Resolve_Table(string flags) => Resolve(flags);

        // An existing but unavailable service is Hidden whatever the other
        // flags say (a missing service is Checking instead — see the table).
        // Pairwise keeps the intent without all 64 combinations.
        [Test, Pairwise]
        public void Unavailable_IsHidden_WhateverElseIsTrue(
            [Values(true, false)] bool isReady,
            [Values(true, false)] bool readyTimedOut,
            [Values(true, false)] bool isSignedIn,
            [Values(true, false)] bool signInPending,
            [Values(true, false)] bool signOutPending,
            [Values(true, false)] bool hasError)
        {
            AccountWidgetState state = XsollaIdAccountWidgetLogic.Resolve(
                serviceExists: true,
                isAvailable: false,
                isReady: isReady,
                readyTimedOut: readyTimedOut,
                isSignedIn: isSignedIn,
                signInPending: signInPending,
                signOutPending: signOutPending,
                hasError: hasError);

            Assert.AreEqual(AccountWidgetState.Hidden, state);
        }

        // ---- ShortName / Shorten ----

        public enum Shortener
        {
            ShortName,
            Shorten
        }

        // Cutting the domain is DisplayNameFormat.LocalPart's job and is tested
        // there; here only that ShortName delegates to it, and that "nothing to
        // show" becomes null.
        [TestCase("user@mail.com", ExpectedResult = "user")]
        [TestCase("   ", ExpectedResult = null)]
        public string ShortName_UsesTheLocalPart(string displayName) =>
            XsollaIdAccountWidgetLogic.ShortName(displayName, 18);

        [TestCase(Shortener.ShortName, "averyveryverylongname", 8, ExpectedResult = "averyve…")]
        [TestCase(Shortener.ShortName, "exact8ch", 8, ExpectedResult = "exact8ch")]
        // A non-positive limit disables truncation.
        [TestCase(Shortener.ShortName, "averyveryverylongname", 0, ExpectedResult = "averyveryverylongname")]
        [TestCase(Shortener.ShortName, "averyveryverylongname", -1, ExpectedResult = "averyveryverylongname")]
        // The limit falls inside the emoji's surrogate pair: drop the whole emoji.
        [TestCase(Shortener.ShortName, "ab\U0001F600cd", 4, ExpectedResult = "ab…")]
        // A username has no '@' to cut at: Shorten keeps it whole.
        [TestCase(Shortener.Shorten, "id12345678", 18, ExpectedResult = "id12345678")]
        [TestCase(Shortener.Shorten, "a@b.c", 18, ExpectedResult = "a@b.c")]
        [TestCase(Shortener.Shorten, "  id1  ", 18, ExpectedResult = "id1")]
        [TestCase(Shortener.Shorten, "id1234567890123", 10, ExpectedResult = "id1234567…")]
        [TestCase(Shortener.Shorten, null, 18, ExpectedResult = null)]
        [TestCase(Shortener.Shorten, "   ", 18, ExpectedResult = null)]
        public string Truncation(Shortener shortener, string value, int maxChars) =>
            shortener == Shortener.ShortName
                ? XsollaIdAccountWidgetLogic.ShortName(value, maxChars)
                : XsollaIdAccountWidgetLogic.Shorten(value, maxChars);

        // ---- ErrorKey ----

        // One explicit row per code. Cancelled (the player closed the window)
        // and SessionExpired are not errors, so they stay silent.
        private static readonly Dictionary<XsollaIdErrorCode, string> ExpectedErrorKeys =
            new Dictionary<XsollaIdErrorCode, string>
            {
                [XsollaIdErrorCode.Cancelled] = null,
                [XsollaIdErrorCode.PopupBlocked] = XsollaIdAccountWidgetLogic.ErrorKeyPopupBlocked,
                [XsollaIdErrorCode.Unavailable] = XsollaIdAccountWidgetLogic.ErrorKeyUnavailable,
                [XsollaIdErrorCode.NotConfigured] = XsollaIdAccountWidgetLogic.ErrorKeyUnavailable,
                [XsollaIdErrorCode.Network] = XsollaIdAccountWidgetLogic.ErrorKeyNetwork,
                [XsollaIdErrorCode.Timeout] = XsollaIdAccountWidgetLogic.ErrorKeyTimeout,
                [XsollaIdErrorCode.InvalidState] = XsollaIdAccountWidgetLogic.ErrorKeyFailed,
                [XsollaIdErrorCode.TokenExchangeFailed] = XsollaIdAccountWidgetLogic.ErrorKeyFailed,
                [XsollaIdErrorCode.SessionExpired] = null,
                [XsollaIdErrorCode.PictureUnavailable] = XsollaIdAccountWidgetLogic.ErrorKeyFailed,
            };

        private static IEnumerable<XsollaIdErrorCode> ErrorCodes => ExpectedErrorKeys.Keys;

        [TestCaseSource(nameof(ErrorCodes))]
        public void ErrorKey(XsollaIdErrorCode code)
        {
            Assert.AreEqual(ExpectedErrorKeys[code], XsollaIdAccountWidgetLogic.ErrorKey(code));
        }

        // A new code must get its own row above, so its mapping (the generic
        // "failed" by default) is a decision, not an accident.
        [Test]
        public void ErrorKey_TableCoversEveryCode()
        {
            CollectionAssert.AreEquivalent(Enum.GetValues(typeof(XsollaIdErrorCode)), ExpectedErrorKeys.Keys);
        }

        // ---- expanded card ----

        // The card shows only while signed in — including SigningOut, so it does
        // not flicker while the token is revoked. Collapsed always wins.
        [Test]
        public void ShowsExpandedPanel(
            [Values] AccountWidgetState state,
            [Values] bool expanded)
        {
            bool signedIn = state == AccountWidgetState.SignedIn || state == AccountWidgetState.SigningOut;

            Assert.AreEqual(expanded && signedIn, XsollaIdAccountWidgetLogic.ShowsExpandedPanel(state, expanded));
        }
    }
}
