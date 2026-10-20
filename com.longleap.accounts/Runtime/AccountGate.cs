using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LongLeap.Core.CloudSaves;
using LongLeap.XsollaId;
using UnityEngine;

namespace LongLeap.Accounts
{
    // Who plays this launch: an account or a guest. AccountId == null is a guest.
    public sealed class AccountGateResult
    {
        public static readonly AccountGateResult Guest = new AccountGateResult(null, null);

        public AccountGateResult(string accountId, IReadOnlyDictionary<string, CloudSaveEntry> accountEntries)
        {
            AccountId = accountId;
            AccountEntries = accountEntries;
        }

        // The signed-in account (Xsolla ID subject); null means guest.
        public string AccountId { get; }

        // The account save as read from the server; null means it was not read
        // (guest) or could not be read (the local mirror is this account's own,
        // see SaveSwitch.CanUseAccount).
        public IReadOnlyDictionary<string, CloudSaveEntry> AccountEntries { get; }

        public bool IsGuest => AccountId == null;
    }

    // Who plays this launch: an Xsolla ID account or a guest.
    //
    // Answers the question BEFORE the save service starts, so the save
    // provider is chosen once: an account gets the cloud save on the server, a
    // guest gets saves on this device only.
    //
    // Order:
    //   1. Wait for the silent session restore (with a timeout).
    //   2. No session — the startup screen: "Sign in" or "Play as guest".
    //   3. Signed in — read the account save. SaveSwitch needs it to merge with
    //      the guest progress. If it can't be read and the local save isn't
    //      this account's, offer to retry or play as a guest.
    //
    // A guest choice is remembered for this app session (statics survive a
    // scene reload); a new launch asks again.
    //
    // Ported from Blood Red Snow's AccountGate: async instead of a coroutine,
    // and the screen is an interface drawn by the game.
    public sealed class AccountGate
    {
        // How long to wait for the session restore. It goes to the network for
        // a fresh token (that request has its own 10 s timeout), so keep a margin.
        private const double RestoreTimeoutSec = 12;

        // How long to wait for the account save to be read.
        private const double PeekTimeoutSec = 15;

        // The player chose guest in this app session.
        private static bool _guestChosen;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _guestChosen = false;

        // Forget the guest choice: the player asked to sign in from the game,
        // and the next run of the gate must show the startup screen.
        public static void ForgetGuestChoice() => _guestChosen = false;

        private readonly IXsollaIdAuth _auth;
        private readonly IAccountCloudSaveScope _scope;
        private readonly Func<string, Task<IReadOnlyDictionary<string, CloudSaveEntry>>> _tryPeek;
        private readonly Func<IAccountScreen> _createScreen;

        private IAccountScreen _screen;

        // tryPeek — read an account's save from the server without applying it;
        // null means it could not be read (XsollaCloudSaveScope.TryPeekAccountAsync).
        // createScreen — build the startup screen; called only when it is needed.
        public AccountGate(
            IXsollaIdAuth auth,
            IAccountCloudSaveScope scope,
            Func<string, Task<IReadOnlyDictionary<string, CloudSaveEntry>>> tryPeek,
            Func<IAccountScreen> createScreen)
        {
            _auth = auth ?? throw new ArgumentNullException(nameof(auth));
            _scope = scope ?? throw new ArgumentNullException(nameof(scope));
            _tryPeek = tryPeek ?? throw new ArgumentNullException(nameof(tryPeek));
            _createScreen = createScreen ?? throw new ArgumentNullException(nameof(createScreen));
        }

        public async Task<AccountGateResult> RunAsync(CancellationToken token = default)
        {
            if (!_auth.IsAvailable)
                return AccountGateResult.Guest;

            if (!await FrameWait.UntilAsync(() => _auth.IsReady, RestoreTimeoutSec, token))
            {
                // Not a silent guest: the startup screen is more honest than an
                // empty guest save for a player who is actually signed in.
                Debug.LogWarning($"{nameof(AccountGate)}: the session restore timed out — showing the startup screen.");
            }

            // Availability can turn out only during the restore: the GamePix SDK
            // reports "not inside the GamePix Player" (localhost, direct link)
            // in its first answer. A screen there would offer a button that
            // cannot work, so this launch is a silent guest.
            if (!_auth.IsAvailable)
            {
                Debug.Log($"{nameof(AccountGate)}: sign-in became unavailable during the session restore — playing as guest.");
                return AccountGateResult.Guest;
            }

            try
            {
                return await LoopAsync(token);
            }
            finally
            {
                if (_screen != null)
                {
                    _screen.Close();
                    _screen = null;
                }
            }
        }

        private async Task<AccountGateResult> LoopAsync(CancellationToken token)
        {
            XsollaIdErrorCode? lastError = null;

            while (true)
            {
                token.ThrowIfCancellationRequested();

                string subject = CurrentSubject();

                if (subject != null)
                {
                    _screen?.ShowBusy(AccountScreenBusy.CheckingSave);

                    IReadOnlyDictionary<string, CloudSaveEntry> entries = await PeekAsync(subject, token);

                    if (SaveSwitch.CanUseAccount(subject, entries != null, _scope))
                    {
                        Debug.Log($"{nameof(AccountGate)}: playing with the Xsolla ID account " +
                            (entries != null ? $"({entries.Count} keys in the cloud save)." : "(the cloud save was not read; using the local copy)."));

                        return new AccountGateResult(subject, entries);
                    }

                    Debug.LogWarning($"{nameof(AccountGate)}: the account save could not be read — offering retry or guest.");

                    bool? retry = null;

                    EnsureScreen();
                    _screen.SignInClicked = null;
                    _screen.RetryClicked = () => retry = true;
                    _screen.GuestClicked = () => retry = false;
                    _screen.ShowServerError();

                    await FrameWait.UntilAsync(() => retry != null, -1, token);

                    if (retry.Value)
                        continue;

                    // The session stays, but this launch runs on the guest save.
                    // The next launch will try again.
                    Debug.Log($"{nameof(AccountGate)}: playing as guest this launch — the account save was unreachable.");
                    _guestChosen = true;
                    return AccountGateResult.Guest;
                }

                if (_guestChosen)
                    return AccountGateResult.Guest;

                // No session — ask.
                Task<XsollaIdResult> signIn = null;
                bool guest = false;

                EnsureScreen();

                // SignInAsync synchronously, straight from the click: see IAccountScreen.
                _screen.SignInClicked = () => signIn ??= _auth.SignInAsync();
                _screen.GuestClicked = () => guest = true;
                _screen.RetryClicked = null;
                _screen.ShowChoice(lastError);

                // The session may still arrive by itself: a restore that hit the
                // timeout sometimes completes after all. Then there is nothing
                // left to ask.
                await FrameWait.UntilAsync(() => signIn != null || guest || CurrentSubject() != null, -1, token);

                if (guest)
                {
                    Debug.Log($"{nameof(AccountGate)}: the player chose to play as a guest.");
                    _guestChosen = true;
                    return AccountGateResult.Guest;
                }

                if (signIn == null)
                    continue;

                _screen.ShowBusy(AccountScreenBusy.SigningIn);

                await FrameWait.UntilAsync(() => signIn.IsCompleted, -1, token);

                XsollaIdResult result = signIn.Status == TaskStatus.RanToCompletion
                    ? signIn.Result
                    : new XsollaIdResult.Failure(
                        XsollaIdErrorCode.Network,
                        signIn.Exception?.GetBaseException().Message ?? "the sign-in task faulted");

                if (result is XsollaIdResult.Failure failure)
                {
                    Debug.LogWarning($"{nameof(AccountGate)}: sign-in failed — {failure.Code}: {failure.Message}");
                    lastError = IsShownError(failure.Code) ? failure.Code : (XsollaIdErrorCode?)null;
                    continue;
                }

                lastError = null;

                // Signed in — the next iteration reads the account save.
            }
        }

        // The player closing the window and an expired session are not errors.
        private static bool IsShownError(XsollaIdErrorCode code) =>
            code != XsollaIdErrorCode.Cancelled && code != XsollaIdErrorCode.SessionExpired;

        private string CurrentSubject()
        {
            XsollaIdSession session = _auth.Current;

            return _auth.IsSignedIn && session != null && session.Subject.IsValid
                ? session.Subject.Value
                : null;
        }

        private void EnsureScreen()
        {
            if (_screen == null)
                _screen = _createScreen();
        }

        private async Task<IReadOnlyDictionary<string, CloudSaveEntry>> PeekAsync(string accountId, CancellationToken token)
        {
            Task<IReadOnlyDictionary<string, CloudSaveEntry>> peek;

            try
            {
                peek = _tryPeek(accountId);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{nameof(AccountGate)}: failed to read the account save — {e.Message}");
                return null;
            }

            if (!await FrameWait.ForAsync(peek, PeekTimeoutSec, token))
            {
                Debug.LogWarning($"{nameof(AccountGate)}: reading the account save exceeded {PeekTimeoutSec} s.");
                return null;
            }

            if (peek.Status != TaskStatus.RanToCompletion)
            {
                Debug.LogWarning($"{nameof(AccountGate)}: failed to read the account save — " +
                    $"{peek.Exception?.GetBaseException().Message}");
                return null;
            }

            return peek.Result;
        }
    }
}
