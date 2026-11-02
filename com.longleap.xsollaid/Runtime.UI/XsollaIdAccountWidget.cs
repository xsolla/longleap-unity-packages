using System;
using System.Collections;
using System.Threading.Tasks;
using LongLeap.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LongLeap.XsollaId.UI
{
    // Xsolla ID account block, typically in the top-right corner of the main
    // menu. One prefab, three child states: "checking", "sign in", "signed in".
    //
    // DOES NOT TOUCH MODALITY. This is not a window: there is no
    // ScreenService.TryOpen/Close here and there must not be — by taking the
    // gate the widget would block the menu itself from opening. It follows the
    // pattern of an "always on" element: subscribe in OnEnable, unsubscribe in
    // OnDisable, redraw on every enable (events did not reach us while the
    // object was disabled).
    //
    // No #if at all: availability comes from IXsollaIdAuth.IsAvailable and the
    // picture from IXsollaIdAvatarLoader, both of which have stubs. The same
    // goes for the BackPack link visibility: it arrives as a flag in Bind,
    // because it depends on the defines of a SPECIFIC game, which the package
    // does not and must not know about.
    //
    // Dependencies arrive through Bind and nothing else. The widget looks up
    // NOTHING by itself: the package does not and must not know a particular
    // game's service locator. Same approach as
    // LongLeap.Settings.UI.SettingsToggleRow.Bind.
    //
    // Bind may be called at any time, including before the first OnEnable and
    // repeatedly: until then the widget shows "checking", not a guest.
    [DisallowMultipleComponent]
    public sealed class XsollaIdAccountWidget : MonoBehaviour
    {
        // Keys of the static labels. We keep KEYS, not resolved strings, so we
        // can redraw ourselves when the language changes.
        private const string KeySignIn = "btn_xsolla_sign_in";
        private const string KeySigningIn = "btn_xsolla_signing_in";
        private const string KeyNameUnknown = "xsolla_name_unknown";
        private const string BackPackUrl = "https://backpack.xsolla.com/";

        [Header("States")]
        [Tooltip("Three mutually exclusive children. All three off means no block " +
            "at all (sign-in unavailable). Do NOT disable the widget object ITSELF: " +
            "a disabled component never re-enables itself or re-queries the service.")]
        [SerializeField] private GameObject checkingRoot;
        [SerializeField] private GameObject signedOutRoot;
        [SerializeField] private GameObject signedInRoot;

        [Header("Sign-in")]
        [SerializeField] private Button signInButton;

        [Tooltip("Sign-in button label. Must be a LocalizedText, not a TMP_Text: " +
            "code switches its key to \"signing in…\", and the component re-translates " +
            "itself when the language changes.")]
        [SerializeField] private LongLeap.Localization.LocalizedText signInLabel;

        [Tooltip("Failure reason line. Empty — the reason goes to the log only.")]
        [SerializeField] private TMP_Text errorLabel;

        [Header("Signed in")]
        [SerializeField] private Image avatarImage;

        [Tooltip("Shown whenever there is no picture: it failed to load, the CDN " +
            "did not serve it, or the account has no picture claim.")]
        [SerializeField] private Sprite avatarPlaceholder;

        [SerializeField] private TMP_Text nameLabel;
        [SerializeField] private Button logOutButton;

        [Tooltip("Link to the Xsolla Backpack. In the prefab it sits NEXT TO the " +
            "three state children, not inside them, so its visibility is toggled " +
            "separately — via a flag in Bind.")]
        [SerializeField] private Button openBackpackUrl;

        [Tooltip("Collapsed row: Xsolla logo and avatar. Clicking it expands the " +
            "card and collapses it again.")]
        [SerializeField] private Button expandButton;

        [Tooltip("Card with the name, username and \"sign out\". Hidden until the " +
            "collapsed row is clicked.")]
        [SerializeField] private GameObject expandedRoot;

        [Tooltip("Username line under the name. The username claim may be absent — " +
            "then the line is hidden entirely rather than left empty.")]
        [SerializeField] private TMP_Text userNameLabel;

        [Header("Font")]
        [Tooltip("Font for every label in the widget. Empty — each label keeps its " +
            "own font, and labels without one use the project's TMP default font " +
            "(TMP Settings). The prefab ships without a font, so set this once " +
            "instead of overriding every label.")]
        [SerializeField] private TMP_FontAsset fontOverride;

        [Header("Settings")]
        [Tooltip("How often to poll IsReady. It has NO event, and the bootstrap " +
            "discards the RestoreAsync task — polling is the only way to learn " +
            "that the answer is final.")]
        [Min(0.02f)] [SerializeField] private float readyPollSeconds = 0.2f;

        [Tooltip("Total time to wait for a final answer before showing the guest " +
            "state. Restore goes over the network; the player should not see " +
            "\"checking\" forever.")]
        [Min(1f)] [SerializeField] private float readyTimeoutSeconds = 12f;

        [Tooltip("How long to keep the error line. 0 — until the next click.")]
        [Min(0f)] [SerializeField] private float errorHoldSeconds = 8f;

        [Tooltip("Maximum characters in the name. Longer names are truncated with " +
            "an ellipsis, otherwise a long e-mail stretches the row across half the screen.")]
        [Min(4)] [SerializeField] private int nameMaxChars = 18;

        // What was passed to Bind. Survives the object being disabled.
        private IXsollaIdAuth _boundAuth;
        private IXsollaIdAvatarLoader _boundAvatars;

        // Whether to show the BackPack link. Defaults to YES so that a widget
        // that has not been bound yet, and the editor preview, look like a build
        // with the link enabled. A game that does not need it passes false
        // explicitly.
        private bool _showBackPackLink = true;

        // What the "Sign in" button does instead of the widget's own sign-in.
        // null means the widget signs in by itself. A game whose sign-in lives on
        // a separate screen (with the save owner chosen before the menu) passes
        // the transition to that screen here.
        private Action _signInRequested;

        // What we are subscribed to right now. null — no subscription.
        private IXsollaIdAuth _account;
        private IXsollaIdAvatarLoader _avatars;
        private Task<XsollaIdResult> _signInTask;
        private Coroutine _readyWatch;
        private Coroutine _signInWatch;
        private Coroutine _signOutWatch;
        private Coroutine _errorTimer;
        private bool _readyTimedOut;
        private bool _signOutPending;

        // Whether the account card is expanded. Memory only and deliberately NOT
        // persisted: an expanded card is a response to a click "right now", not
        // a setting. Open after a page reload it would look stuck.
        private bool _expanded;

        // A KEY, not a resolved string: otherwise the previous language would
        // stay on screen after a language change.
        private string _errorKey;

        // The loaded avatar. The sprite is SHARED and owned by the loader — do
        // not destroy or modify it, see IXsollaIdAvatarLoader.
        private Sprite _avatar;
        private AvatarUrl _avatarUrl;

        void Awake()
        {
            if (checkingRoot == null || signedOutRoot == null || signedInRoot == null)
            {
                Debug.LogError(
                    "XsollaIdAccountWidget: the state objects are not assigned " +
                    "(checkingRoot/signedOutRoot/signedInRoot) — nothing to draw.", this);
            }

            if (signInButton == null)
                Debug.LogError("XsollaIdAccountWidget: the sign-in button is not assigned.", this);

            if (logOutButton == null)
                Debug.LogError("XsollaIdAccountWidget: the sign-out button is not assigned.", this);

            if (openBackpackUrl == null)
                Debug.LogError("XsollaIdAccountWidget: the BackPack button is not assigned.", this);

            // Duplicated prefab: the reference may point at another copy's object.
            if (signInLabel != null && !signInLabel.transform.IsChildOf(transform))
            {
                Debug.LogError(
                    "XsollaIdAccountWidget: signInLabel points outside the widget — " +
                    "the prefab was probably duplicated.", this);
            }

            if (expandButton == null)
                Debug.LogError("XsollaIdAccountWidget: the expand button is not assigned.", this);

            if (expandedRoot == null)
            {
                Debug.LogError(
                    "XsollaIdAccountWidget: expandedRoot is not assigned — nothing to expand, " +
                    "and nowhere to show the name and username.", this);
            }

            // Click handlers are attached here, once, with a RemoveListener
            // first, so re-enabling the object does not create duplicates.
            Wire(signInButton, OnSignInClicked);
            Wire(logOutButton, OnLogOutClicked);
            Wire(openBackpackUrl, OnOpenBackPackClicked);
            Wire(expandButton, OnExpandClicked);

            ApplyFontOverride();
        }

        // Once, in Awake: labels inside disabled states are included, so a state
        // switched on later already has the right font.
        //
        // The font setter also swaps the material to the font's default one,
        // which is what a label without its own material needs.
        private void ApplyFontOverride()
        {
            if (fontOverride == null)
                return;

            foreach (TMP_Text text in GetComponentsInChildren<TMP_Text>(includeInactive: true))
                text.font = fontOverride;
        }

        void OnEnable()
        {
            // The name and error line are set from code, so subscribing to
            // language changes is required — and so is the matching unsubscribe.
            Loc.OnLanguageChanged += Apply;

            SubscribeAccount();
            RefreshAvatar();
            Apply();

            StartReadyWatch();
        }

        void OnDisable()
        {
            Loc.OnLanguageChanged -= Apply;

            UnbindAccount();

            StopWatch(ref _readyWatch);
            StopWatch(ref _signInWatch);
            StopWatch(ref _signOutWatch);
            StopWatch(ref _errorTimer);

            // The sign-in task is NOT cancelled — SignInAsync has no
            // cancellation. We drop the reference: on re-enable Apply will see
            // IsSignedIn, and a repeated click returns THE SAME task by contract
            // and does not open a second window.
            _signInTask = null;
            _signOutPending = false;

            // The menu was closed — collapse the card. Otherwise it would greet
            // the player expanded next time, although they never opened it.
            _expanded = false;
        }

        // Safety net: OnDisable may not arrive when the scene is unloaded.
        void OnDestroy() => UnbindAccount();

        // Hand the widget the sign-in service and the avatar loader.
        //
        // May be called at any time: before the object is first enabled or
        // after. Until then the widget shows "checking" — declaring a player we
        // know nothing about a guest would be wrong.
        //
        // showBackPackLink — whether to show the BackPack button. The game
        // decides: BackPack is a separate Xsolla product, and in a build that
        // does not work with it the link leads nowhere. A define cannot decide
        // this here: the package goes into other projects that have no such
        // define.
        //
        // signInRequested replaces the widget's own sign-in on the "Sign in"
        // button (see _signInRequested). null means the widget signs in by
        // itself, as before.
        public void Bind(
            IXsollaIdAuth auth, IXsollaIdAvatarLoader avatars, bool showBackPackLink = true,
            Action signInRequested = null)
        {
            _boundAuth = auth;
            _boundAvatars = avatars;
            _showBackPackLink = showBackPackLink;
            _signInRequested = signInRequested;

            if (!isActiveAndEnabled)
                return;

            SubscribeAccount();
            RefreshAvatar();
            Apply();
            StartReadyWatch();
        }

        void SubscribeAccount()
        {
            if (_account != null)
                return;

            // Not an error: the scene owner may not have called Bind yet — the
            // watch coroutine waits and subscribes then.
            if (_boundAuth == null)
                return;

            _account = _boundAuth;
            _avatars = _boundAvatars;

            _account.SignedIn += OnSignedIn;
            _account.SignedOut += OnSignedOut;
            _account.SessionUpdated += OnSessionUpdated;
        }

        void UnbindAccount()
        {
            if (_account == null)
                return;

            _account.SignedIn -= OnSignedIn;
            _account.SignedOut -= OnSignedOut;
            _account.SessionUpdated -= OnSessionUpdated;

            // Only the subscription is cleared. What was passed to Bind is kept:
            // the object may be disabled and enabled again, and nobody will call
            // Bind a second time.
            _account = null;
        }

        void OnSignedIn(XsollaIdSession session)
        {
            _errorKey = null;
            StopWatch(ref _readyWatch);
            RefreshAvatar();
            Apply();
        }

        void OnSignedOut()
        {
            // Someone else's face and name after sign-out is the most visible
            // bug such a block can have.
            _avatar = null;
            _avatarUrl = AvatarUrl.None;
            _signOutPending = false;

            // The card belonged to the player who signed out: leaving it open
            // would show the next player someone else's name and username.
            _expanded = false;

            Apply();
        }

        // Token refreshed or profile topped up: same player, new session
        // object. The picture may have appeared only now.
        void OnSessionUpdated(XsollaIdSession session)
        {
            RefreshAvatar();
            Apply();
        }

        // ---- waiting for the final answer ----

        void StartReadyWatch()
        {
            if (_readyWatch != null)
                return;

            // The answer is already final — nothing to wait for.
            if (_account != null && (!_account.IsAvailable || _account.IsReady))
                return;

            _readyWatch = StartCoroutine(WatchReady());
        }

        // IsReady has no event: RestoreAsync sets it in finally, and the caller
        // usually fires the task and forgets it — nobody has a handle to await.
        // On top of that, if there was nothing to restore, RestoreAsync returns
        // Expired and SignedOut never fires. So polling is the only way to know
        // that "guest" is the final answer.
        //
        // A coroutine rather than Update with a flag: Update would tick every
        // frame for the widget's whole life, while the coroutine ends exactly
        // when the answer arrives and dies with the object.
        IEnumerator WatchReady()
        {
            // Real time: the menu may run while game time is frozen
            // (timeScale = 0), where WaitForSeconds does not tick.
            float deadline = Time.unscaledTime + readyTimeoutSeconds;

            while (true)
            {
                // The service may have just appeared — subscribe right away.
                SubscribeAccount();

                if (_account != null && (!_account.IsAvailable || _account.IsReady))
                    break;

                if (Time.unscaledTime >= deadline)
                {
                    _readyTimedOut = true;

                    Debug.LogWarning(
                        $"XsollaIdAccountWidget: session restore did not answer in " +
                        $"{readyTimeoutSeconds:0} s — showing the player as a guest.", this);
                    break;
                }

                yield return new WaitForSecondsRealtime(readyPollSeconds);
            }

            _readyWatch = null;

            RefreshAvatar();
            Apply();
        }

        // ---- sign-in ----

        // WARNING: SignInAsync is called SYNCHRONOUSLY, right after the checks.
        // There must be no await before it — the browser would treat the popup
        // as unsolicited and block it. Working example: XsollaIdDebugPanel.
        void OnSignInClicked()
        {
            if (_account == null || !_account.IsAvailable)
            {
                Debug.LogError(
                    "XsollaIdAccountWidget: sign-in clicked while the service is missing " +
                    "or unavailable — the button should not be here.", this);
                return;
            }

            // The game owns sign-in — hand it the click and do nothing else.
            if (_signInRequested != null)
            {
                _signInRequested();
                return;
            }

            // The window is already open. By contract a repeated SignInAsync
            // would return the same task and not open a second window, but the
            // extra click is swallowed here anyway.
            if (_signInTask != null && !_signInTask.IsCompleted)
                return;

            StopWatch(ref _errorTimer);
            _errorKey = null;

            _signInTask = _account.SignInAsync();

            Apply();

            StopWatch(ref _signInWatch);
            _signInWatch = StartCoroutine(WatchSignIn(_signInTask));
        }

        // The task is awaited by a COROUTINE, not async void. Not a matter of
        // style: the scene may be reloaded meanwhile, and an async method's
        // continuation would write into a destroyed object
        // (MissingReferenceException a frame later). A coroutine dies together
        // with the GameObject.
        IEnumerator WatchSignIn(Task<XsollaIdResult> task)
        {
            while (!task.IsCompleted)
                yield return null;

            _signInWatch = null;

            if (_signInTask == task)
                _signInTask = null;

            // IXsollaIdAuth NEVER throws, but a task broken by something else
            // must not take the widget down.
            XsollaIdResult result = task.Status == TaskStatus.RanToCompletion
                ? task.Result
                : new XsollaIdResult.Failure(
                    XsollaIdErrorCode.Network,
                    task.Exception?.GetBaseException().Message ?? "sign-in task faulted");

            if (result is XsollaIdResult.Success)
            {
                Debug.Log("XsollaIdAccountWidget: the player signed in with Xsolla ID.");

                _errorKey = null;

                // SignedIn may have arrived as an event earlier — Resolve accounts for that.
                RefreshAvatar();
                Apply();
                yield break;
            }

            var failure = (XsollaIdResult.Failure)result;

            // ALWAYS log, including the silent outcomes: silence is for the
            // player, not for the log.
            Debug.LogWarning(
                $"XsollaIdAccountWidget: sign-in failed — {failure.Code}: {failure.Message}");

            _errorKey = XsollaIdAccountWidgetLogic.ErrorKey(failure.Code);

            Apply();

            if (!string.IsNullOrEmpty(_errorKey) && errorHoldSeconds > 0f)
            {
                StopWatch(ref _errorTimer);
                _errorTimer = StartCoroutine(HideErrorAfterDelay());
            }
        }

        IEnumerator HideErrorAfterDelay()
        {
            yield return new WaitForSecondsRealtime(errorHoldSeconds);

            _errorTimer = null;
            _errorKey = null;

            Apply();
        }

        // ---- expanding the card ----

        // No modality and no catching clicks outside the card. A full-screen
        // trap would collapse it "the usual way", but the widget sits on top of
        // the main menu and would eat clicks meant for it — and this block
        // deliberately never touches modality (see the class comment). So the
        // card closes with the same click that opened it.
        void OnExpandClicked()
        {
            if (_account == null || !_account.IsSignedIn)
                return;

            _expanded = !_expanded;

            Apply();
        }

        // ---- sign-out ----

        // Opened straight from the click handler: in WebGL the browser allows a
        // new tab only as a direct response to a user gesture.
        void OnOpenBackPackClicked() => Application.OpenURL(BackPackUrl);

        void OnLogOutClicked()
        {
            if (_account == null || !_account.IsSignedIn || !_account.CanSignOut)
                return;

            // No confirmation, by design: sign-out is reversible — the guest
            // save lives separately and comes back unchanged after sign-out,
            // while the account's progress stays with the account. A double
            // sign-out is still suppressed.
            if (_signOutPending)
                return;

            Debug.Log("XsollaIdAccountWidget: signing out.");

            _signOutPending = true;
            Apply();

            // The service always performs the local part of sign-out, even if
            // server-side token revocation fails — SignedOut will arrive and
            // redraw us. We still wait for the task: if the server is down, the
            // button must not stay greyed out forever.
            StopWatch(ref _signOutWatch);
            _signOutWatch = StartCoroutine(WatchSignOut(_account.SignOutAsync()));
        }

        IEnumerator WatchSignOut(Task task)
        {
            while (!task.IsCompleted)
                yield return null;

            _signOutWatch = null;
            _signOutPending = false;

            Apply();
        }

        // ---- avatar ----

        // The widget PULLS the picture from the loader itself: the address lives
        // in the session, and the ready sprite in the loader's cache, which
        // survives scene reloads.
        void RefreshAvatar()
        {
            AvatarUrl url = _account?.Current?.Picture ?? AvatarUrl.None;

            if (url == _avatarUrl && _avatar != null)
                return;

            _avatarUrl = url;
            _avatar = null;

            if (_avatars == null || !url.IsValid)
                return;

            // Already in memory — take it without waiting, so the placeholder
            // does not flash for a frame after a scene reload.
            if (_avatars.TryGetCached(url, out Sprite cached))
            {
                _avatar = cached;
                return;
            }

            StartCoroutine(WatchAvatar(url, _avatars.LoadAsync(url)));
        }

        IEnumerator WatchAvatar(AvatarUrl url, Task<AvatarResult> task)
        {
            while (!task.IsCompleted)
                yield return null;

            // While downloading, the player may have signed out or signed in as
            // someone else — then the result is no longer about them.
            if (url != _avatarUrl)
                yield break;

            if (task.Status == TaskStatus.RanToCompletion && task.Result is AvatarResult.Success ok)
            {
                _avatar = ok.Sprite;
                Apply();
                yield break;
            }

            // The placeholder stays: the player is not told the picture is
            // missing. But the console ALWAYS gets a line — it is for the
            // developer (this is how we found out Xsolla ID serves webp, which
            // DownloadHandlerTexture cannot decode).
            var failure = task.Result as AvatarResult.Failure;

            if (failure != null)
                Debug.LogWarning($"XsollaIdAccountWidget: avatar unavailable — {failure.Code}: {failure.Message}");
        }

        // ---- rendering ----

        // The ONLY place where state turns into visuals. All events, timers and
        // clicks only change fields and call Apply — there are deliberately no
        // two paths to the screen.
        void Apply()
        {
            AccountWidgetState state = XsollaIdAccountWidgetLogic.Resolve(
                serviceExists: _account != null,
                isAvailable: _account != null && _account.IsAvailable,
                isReady: _account != null && _account.IsReady,
                readyTimedOut: _readyTimedOut,
                isSignedIn: _account != null && _account.IsSignedIn,
                signInPending: _signInTask != null && !_signInTask.IsCompleted,
                signOutPending: _signOutPending,
                hasError: !string.IsNullOrEmpty(_errorKey));

            SetActiveSafe(checkingRoot, state == AccountWidgetState.Checking);

            SetActiveSafe(
                signedOutRoot,
                state == AccountWidgetState.SignedOut ||
                state == AccountWidgetState.SigningIn ||
                state == AccountWidgetState.SignedOutWithError);

            SetActiveSafe(
                signedInRoot,
                state == AccountWidgetState.SignedIn ||
                state == AccountWidgetState.SigningOut);

            if (signInLabel != null)
                signInLabel.SetKey(state == AccountWidgetState.SigningIn ? KeySigningIn : KeySignIn);

            if (errorLabel != null)
            {
                bool showError = state == AccountWidgetState.SignedOutWithError;

                SetActiveSafe(errorLabel.gameObject, showError);

                if (showError)
                    errorLabel.text = Loc.Resolve(_errorKey);
            }

            SetActiveSafe(
                expandedRoot,
                XsollaIdAccountWidgetLogic.ShowsExpandedPanel(state, _expanded));

            // The session is not cached: a token refresh replaces Current with a
            // new object, so the live value must be read.
            XsollaIdSession current = _account?.Current;

            if (nameLabel != null)
            {
                // The profile name comes first, DisplayName is the fallback. The
                // account may have no name claim at all, and an empty string in
                // place of a name looks like broken layout, so the fallback is
                // mandatory: DisplayName is picked from four claims and is
                // almost never empty.
                string shown = XsollaIdAccountWidgetLogic.ShortName(current?.FullName, nameMaxChars)
                               ?? XsollaIdAccountWidgetLogic.ShortName(current?.DisplayName, nameMaxChars);

                nameLabel.text = shown ?? Loc.Resolve(KeyNameUnknown);
            }

            if (userNameLabel != null)
            {
                // Shorten, not ShortName: a username is not an e-mail address and
                // has no '@' to cut at — but its length still needs a limit, or a
                // generated id<digits> stretches the card.
                string user = XsollaIdAccountWidgetLogic.Shorten(current?.UserName, nameMaxChars);

                // Hide the whole line: an empty one would leave a gap exactly
                // where the player expects a value.
                SetActiveSafe(userNameLabel.gameObject, user != null);

                if (user != null)
                    userNameLabel.text = user;
            }

            if (avatarImage != null)
            {
                Sprite sprite = _avatar != null ? _avatar : avatarPlaceholder;

                avatarImage.sprite = sprite;
                avatarImage.enabled = sprite != null;
            }

            if (logOutButton != null)
            {
                logOutButton.interactable = !_signOutPending;

                // Where the platform owns sign-in there is no sign-out at all —
                // hide it rather than disable it, like everything else unavailable.
                SetActiveSafe(logOutButton.gameObject, _account == null || _account.CanSignOut);
            }

            // HIDE rather than disable — like the whole widget when sign-in is
            // unavailable.
            //
            // Done here even though the other buttons do not control visibility
            // at all: BackPackBtn is a child of the prefab ROOT, next to the
            // three states, not inside signedInRoot. No state switch touches it,
            // and the root itself is never disabled — without this line the
            // button would always be visible, including in Hidden.
            if (openBackpackUrl != null)
                SetActiveSafe(openBackpackUrl.gameObject, _showBackPackLink);
        }

        // ---- helpers ----

        static void Wire(Button button, UnityEngine.Events.UnityAction handler)
        {
            if (button == null)
                return;

            button.onClick.RemoveListener(handler);
            button.onClick.AddListener(handler);
        }

        // SetActive only on change: a redundant call rebuilds the canvas layout.
        static void SetActiveSafe(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
                target.SetActive(active);
        }

        void StopWatch(ref Coroutine routine)
        {
            if (routine == null)
                return;

            StopCoroutine(routine);
            routine = null;
        }

#if UNITY_EDITOR
        // Layout preview in the editor: component gear menu → Preview: …
        //
        // Needed because real sign-in is NEVER available in the editor:
        // XsollaIdWebBrowser.IsSupported lives under #if UNITY_WEBGL &&
        // !UNITY_EDITOR, so the factory returns UnavailableXsollaIdAuth and the
        // widget hides itself. The "signed in" layout could not be seen otherwise.
        //
        // Call it on an INSTANCE in a scene, not in prefab edit mode — otherwise
        // SetActive is saved into the prefab as an override.
        // The username deliberately looks real: Xsolla ID gives an account
        // without its own username a generated id<digits>, and the preview
        // should show exactly that pair, not two similar words.
        private const string PreviewUserName = "id19334455";
        private const string PreviewFullName = "Editor User";

        [ContextMenu("Preview: signed in")]
        private void PreviewSignedIn() => PreviewWith(
            FakeXsollaIdAuth.SignedInAs("user@mail.com", PreviewUserName, PreviewFullName));

        // The expanded card is a separate item: expansion lives in a field, not
        // in the service, so it cannot be seen in the editor otherwise — button
        // clicks do not work in edit mode.
        [ContextMenu("Preview: signed in (expanded)")]
        private void PreviewSignedInExpanded()
        {
            PreviewWith(FakeXsollaIdAuth.SignedInAs("user@mail.com", PreviewUserName, PreviewFullName));

            _expanded = true;

            Apply();
        }

        // Account without a name claim: the name must fall back, and the
        // username line must disappear entirely. This is exactly where the
        // layout breaks if empty values are not hidden.
        [ContextMenu("Preview: signed in (expanded, no claims)")]
        private void PreviewSignedInExpandedBare()
        {
            PreviewWith(FakeXsollaIdAuth.SignedInAs("user@mail.com"));

            _expanded = true;

            Apply();
        }

        [ContextMenu("Preview: signed out")]
        private void PreviewSignedOut() => PreviewWith(FakeXsollaIdAuth.Guest());

        [ContextMenu("Preview: checking")]
        private void PreviewChecking() => PreviewWith(FakeXsollaIdAuth.Restoring());

        [ContextMenu("Preview: error (popup blocked)")]
        private void PreviewError()
        {
            PreviewWith(FakeXsollaIdAuth.Guest());

            _errorKey = XsollaIdAccountWidgetLogic.ErrorKeyPopupBlocked;

            Apply();
        }

        [ContextMenu("Preview: hidden (unavailable)")]
        private void PreviewHidden() => PreviewWith(FakeXsollaIdAuth.Unavailable());

        private void PreviewWith(IXsollaIdAuth fake)
        {
            UnbindAccount();

            _account = fake;
            _avatars = null;
            _readyTimedOut = false;
            _errorKey = null;
            _signOutPending = false;
            _signInTask = null;
            _avatar = null;
            _avatarUrl = AvatarUrl.None;
            _expanded = false;

            Apply();
        }
#endif
    }
}
