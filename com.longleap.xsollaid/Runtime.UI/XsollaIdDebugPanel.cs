#if XSOLLA_ID_DEBUG
using System;
using System.Threading.Tasks;
using UnityEngine;

namespace LongLeap.XsollaId.UI
{
    // On-screen panel for testing sign-in: state, the four name claims, and a
    // button per entry point.
    //
    // The whole file sits behind the XSOLLA_ID_DEBUG define and never reaches a
    // build without it: this is a debugging tool, not a feature. Toggle it from
    // Xsolla ID / Optional / Debug Panel in the editor menu.
    //
    // Lives in the package and knows NOTHING about any particular game -- same
    // rule as XsollaIdAccountWidget. The service arrives through the provider
    // passed to Spawn, so the only game-side part is a few lines saying where
    // that game keeps its auth. A direct reference to a service locator here
    // would tie the package to one project and block reuse.
    //
    // Sign-in can only be tested in a game built for WebGL: in the editor there
    // is no popup window at all (IsSupported returns false), so the sequence is
    // LongLeap / Build Manager, then python serve.py and http://127.0.0.1:8081/
    // (127.0.0.1 exactly -- localhost is a different origin and OriginGuard
    // will not let it through).
    //
    // Unity logs go to the browser console in WebGL -- keep it open.
    public sealed class XsollaIdDebugPanel : MonoBehaviour
    {
        private const float PanelWidth = 300f;
        private const float Margin = 10f;

        // The panel is laid out for a 720p-tall screen and scaled up from
        // there, otherwise IMGUI renders unreadably small on a large canvas.
        private const float DesignHeight = 720f;

        // Instance fields, not static: the scene reloads on death, and a stale
        // style left over from a previous load can point at a dead font.
        private GUIStyle _label;
        private GUIStyle _title;

        private bool _collapsed;
        private bool _busy;
        private string _lastResult = "not attempted yet";

        // Where to get the service. A delegate rather than the service itself:
        // the panel spawns before the bootstrap has built anything, so a value
        // captured once would stay null for the whole session.
        private Func<IXsollaIdAuth> _provider;

        // The live panel, so a second Spawn re-points the existing one instead
        // of stacking another copy on top of it.
        //
        // Static, and that is safe here only because the object it points at is
        // DontDestroyOnLoad: both survive the scene reload on death together, so
        // they cannot disagree. OnDestroy clears it anyway.
        private static XsollaIdDebugPanel _instance;

        // Created from code so debugging costs no scene or prefab edits.
        //
        // Call it from the game once, handing over a way to reach the service --
        // whatever that game uses to hold it:
        //
        //   XsollaIdDebugPanel.Spawn(() => MyServices.Instance?.Account);
        public static void Spawn(Func<IXsollaIdAuth> provider)
        {
            if (provider == null)
            {
                Debug.LogError(
                    "XsollaIdDebugPanel: Spawn got no provider — there would be no way " +
                    "to reach the service, and the panel would show nothing.");
                return;
            }

            // Already up: re-point it. Two panels would draw on top of each
            // other and both would react to the same click.
            if (_instance != null)
            {
                _instance._provider = provider;
                return;
            }

            var go = new GameObject(nameof(XsollaIdDebugPanel));
            _instance = go.AddComponent<XsollaIdDebugPanel>();
            _instance._provider = provider;
            DontDestroyOnLoad(go);

            Debug.Log(
                "XsollaIdDebugPanel: on-screen debug panel enabled (top-left corner). " +
                "Debug only, guarded by the XSOLLA_ID_DEBUG define.");
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void OnGUI()
        {
            EnsureStyles();

            float scale = Mathf.Max(1f, Screen.height / DesignHeight);
            Matrix4x4 restore = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            GUILayout.BeginArea(new Rect(Margin, Margin, PanelWidth, Screen.height / scale - Margin * 2f));
            GUILayout.BeginVertical(GUI.skin.box);

            DrawHeader();

            if (!_collapsed)
                DrawBody();

            GUILayout.EndVertical();
            GUILayout.EndArea();

            GUI.matrix = restore;
        }

        private void DrawHeader()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Xsolla ID debug", _title);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button(_collapsed ? "+" : "-", GUILayout.Width(24f)))
                _collapsed = !_collapsed;

            GUILayout.EndHorizontal();
        }

        private void DrawBody()
        {
            // Read without the logging accessor: OnGUI runs every frame, and a
            // missing service must not spam the console once per frame.
            IXsollaIdAuth account = _provider?.Invoke();

            if (account == null)
            {
                GUILayout.Label("the service is not there yet (still starting up?)", _label);
                return;
            }

            GUILayout.Label($"IsAvailable: {account.IsAvailable}", _label);
            GUILayout.Label($"IsReady: {account.IsReady}", _label);
            GUILayout.Label($"IsSignedIn: {account.IsSignedIn}", _label);
            GUILayout.Label($"DisplayName: {account.Current?.DisplayName ?? "-"}", _label);
            GUILayout.Label($"Can refresh: {account.Current?.CanRefresh ?? false}", _label);
            GUILayout.Label($"Last result: {_lastResult}", _label);

            // Only for a signed-in player: the snapshot is static and outlives
            // the scene reload, so after a sign-out it would be someone else's.
            if (account.IsSignedIn)
                DrawClaims();

            GUILayout.Space(6f);

            // Interactive path: opens the Xsolla ID window.
            //
            // The popup must be opened from the click itself. GUILayout.Button
            // reports the press inside the same input frame, so SignIn() runs
            // synchronously from here -- do not await anything before it, or
            // the browser will treat the window as unsolicited and block it.
            GUI.enabled = !_busy && !account.IsSignedIn;

            if (GUILayout.Button("Sign in (window)"))
                SignIn();

            // Silent path: the one the game already runs once at start. Opens
            // no window, so it is safe to fire from anywhere. Useful to re-run
            // the restore without reloading the page.
            GUI.enabled = !_busy;

            if (GUILayout.Button("Restore (silent)"))
                Restore();

            // Forces a token refresh. This is the direct test for the `offline`
            // scope: with no refresh token issued, it comes back Expired.
            GUI.enabled = !_busy && account.IsSignedIn;

            if (GUILayout.Button("Refresh token"))
                Refresh();

            if (GUILayout.Button("Sign out"))
                SignOut();

            GUI.enabled = true;

            if (GUILayout.Button("Log status to console"))
                LogStatus();
        }

        // The four claims PickDisplayName chooses between, in its own priority
        // order: the first non-empty one becomes DisplayName. Worth seeing
        // because Xsolla ID hands every account an auto-generated `username`
        // (id<digits>) unless one was picked explicitly, and `username` is
        // first -- so a name typed into the Xsolla profile can be sitting in a
        // claim the picker never reaches.
        //
        // `?` means userinfo was never asked in this session, `-` means it was
        // asked and the claim is absent. The key list underneath is names only,
        // no values: enough to spot a `nickname` we do not read, without
        // printing anything nobody asked for.
        private void DrawClaims()
        {
            GUILayout.Space(4f);
            GUILayout.Label("userinfo claims", _title);

            if (!XsollaIdDebugClaims.HasSnapshot)
            {
                GUILayout.Label(
                    "userinfo has not run in this session, so DisplayName came from the stored " +
                    "session (ProfileFetched), not from the server. Sign out and back in to force it.",
                    _label);
                return;
            }

            foreach (string key in XsollaIdDebugClaims.Watched)
                GUILayout.Label($"{key}: {XsollaIdDebugClaims.Value(key)}", _label);

            GUILayout.Label($"claims present: {string.Join(", ", XsollaIdDebugClaims.AllKeys)}", _label);
        }

        private void EnsureStyles()
        {
            if (_label != null)
                return;

            _label = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            _title = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
        }

        private void SignIn()
        {
            IXsollaIdAuth account = Require();

            if (account == null)
                return;

            Debug.Log("XsollaIdDebugPanel: starting sign-in…");

            _busy = true;
            _lastResult = "signing in…";

            _ = ReportAsync("sign-in", account.SignInAsync());
        }

        // The silent path -- restores a stored session and never opens a
        // window. The game already runs this once at start; the button is here
        // so it can be re-run without reloading the page.
        private void Restore()
        {
            IXsollaIdAuth account = Require();

            if (account == null)
                return;

            Debug.Log("XsollaIdDebugPanel: restoring stored session…");

            _busy = true;
            _lastResult = "restoring…";

            _ = ReportAsync("restore", account.RestoreAsync());
        }

        // Forces a token refresh against the stored refresh token.
        private void Refresh()
        {
            IXsollaIdAuth account = Require();

            if (account == null)
                return;

            Debug.Log("XsollaIdDebugPanel: refreshing access token…");

            _busy = true;
            _lastResult = "refreshing…";

            _ = ReportAsync("refresh", account.RefreshAsync());
        }

        private void SignOut()
        {
            IXsollaIdAuth account = Require();

            if (account == null)
                return;

            Debug.Log("XsollaIdDebugPanel: signing out…");

            _lastResult = "signed out";

            _ = account.SignOutAsync();
        }

        private void LogStatus()
        {
            IXsollaIdAuth account = _provider?.Invoke();

            if (account == null)
            {
                Debug.LogWarning("XsollaIdDebugPanel: the service does not exist yet.");
                return;
            }

            Debug.Log(
                $"XsollaIdDebugPanel: IsAvailable={account.IsAvailable}, IsReady={account.IsReady}, " +
                $"IsSignedIn={account.IsSignedIn}, DisplayName={account.Current?.DisplayName ?? "-"}");

            // The same claims the panel shows. Duplicated into the console on
            // purpose: on WebGL that is where a bug report gets copied from, and
            // a screenshot of the panel is not.
            if (!account.IsSignedIn)
                return;

            if (!XsollaIdDebugClaims.HasSnapshot)
            {
                Debug.Log(
                    "XsollaIdDebugPanel: userinfo has not run in this session — DisplayName came " +
                    "from the stored session (ProfileFetched).");
                return;
            }

            var claims = new System.Text.StringBuilder("XsollaIdDebugPanel: userinfo claims —");

            foreach (string key in XsollaIdDebugClaims.Watched)
                claims.Append($" {key}={XsollaIdDebugClaims.Value(key)},");

            claims.Append($" claims present: {string.Join(", ", XsollaIdDebugClaims.AllKeys)}");

            Debug.Log(claims.ToString());
        }

        // Logging accessor for the buttons: unlike the one the panel reads,
        // this one explains loudly why nothing is going to happen.
        //
        // Instance method, not static: the provider belongs to the panel.
        private IXsollaIdAuth Require()
        {
            IXsollaIdAuth account = _provider?.Invoke();

            if (account == null)
            {
                Debug.LogError("XsollaIdDebugPanel: the provider returned no service.");
                return null;
            }

            if (!account.IsAvailable)
            {
                Debug.LogError(
                    "XsollaIdDebugPanel: sign-in is unavailable (IsAvailable == false). Check that " +
                    "clientId is filled in in the Xsolla ID config asset and that the game runs " +
                    "as a WebGL build, not in the editor.");
                return null;
            }

            return account;
        }

        // Shared by all three result-returning calls; `what` names the one that
        // produced the result so the console line stays unambiguous.
        private async Task ReportAsync(string what, Task<XsollaIdResult> task)
        {
            XsollaIdResult result = await task;

            _busy = false;

            if (result is XsollaIdResult.Success success)
            {
                _lastResult = $"{what}: OK, {success.Session.DisplayName ?? "no name"}";

                Debug.Log(
                    $"XsollaIdDebugPanel: {what} SUCCEEDED. displayName=" +
                    $"{success.Session.DisplayName ?? "-"}, subject={success.Session.Subject}, " +
                    $"has refresh token={success.Session.CanRefresh}");
                return;
            }

            var failure = (XsollaIdResult.Failure)result;

            _lastResult = $"{what}: {failure.Code}";

            Debug.LogWarning($"XsollaIdDebugPanel: {what} failed — {failure.Code}: {failure.Message}");
        }
    }
}
#endif
