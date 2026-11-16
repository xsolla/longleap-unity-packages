using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LongLeap.XsollaId.Web
{
    // Opens the sign-in window and receives the answer from JS.
    //
    // The bridge is one-way in each direction: C# → JS via
    // [DllImport("__Internal")], JS → C# via SendMessage to a receiver object.
    // The vendor Xsolla SDK uses the same approach; there is no reason to
    // invent another.
    public sealed class XsollaIdWebBrowser : IXsollaIdBrowser
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void OpenXsollaIdPopup(string url);
#else
        // In the editor and on other platforms there is no window: IsSupported
        // returns false and the call is never reached. The stub exists only so
        // the code compiles.
        private static void OpenXsollaIdPopup(string url) =>
            throw new NotSupportedException("XsollaIdWebBrowser: the popup window exists only in a WebGL build.");
#endif

        public bool IsSupported =>
#if UNITY_WEBGL && !UNITY_EDITOR
            true;
#else
            false;
#endif

        public Task<BrowserAuthResult> AuthorizeAsync(string authorizeUrl, string expectedState, TimeSpan timeout)
        {
            if (!IsSupported)
            {
                return Task.FromResult(BrowserAuthResult.Failed(
                    XsollaIdErrorCode.Unavailable,
                    "sign-in is available only in a browser build (there is no window in the editor)."));
            }

            XsollaIdCallbackReceiver receiver = XsollaIdCallbackReceiver.Instance;

            // One sign-in at a time. XsollaIdAuth already returns the same task
            // on a repeated click, but there is one receiver for the whole game,
            // and a second concurrent sign-in would mix up the answers.
            if (!receiver.TryBegin(out TaskCompletionSource<BrowserAuthResult> completion))
            {
                return Task.FromResult(BrowserAuthResult.Failed(
                    XsollaIdErrorCode.Cancelled, "sign-in is already in progress."));
            }

            receiver.ArmTimeout(timeout);

            try
            {
                // Synchronously and as the last action: the player's gesture is
                // still "hot".
                OpenXsollaIdPopup(authorizeUrl);
            }
            catch (Exception ex)
            {
                receiver.Complete(BrowserAuthResult.Failed(
                    XsollaIdErrorCode.PopupBlocked, $"could not open the sign-in window: {ex.Message}"));
            }

            return completion.Task;
        }
    }

    // Receives messages from JS. Lives in DontDestroyOnLoad because the sign-in
    // window can outlive a scene reload, and the answer must arrive either way.
    internal sealed class XsollaIdCallbackReceiver : MonoBehaviour
    {
        // Object name SendMessage in XsollaId.jslib uses to find it.
        internal const string ReceiverName = "XsollaIdCallbacks";

        private TaskCompletionSource<BrowserAuthResult> _completion;
        private CancellationTokenSource _timeout;

        private static XsollaIdCallbackReceiver _instance;

        public static XsollaIdCallbackReceiver Instance
        {
            get
            {
                if (_instance == null)
                {
                    // Fixed name: SendMessage in the jslib finds the object by
                    // it. Change only together with XsollaId.jslib.
                    _instance = new GameObject(ReceiverName).AddComponent<XsollaIdCallbackReceiver>();
                }

                return _instance;
            }
        }

        private void Awake() => DontDestroyOnLoad(gameObject);

        public bool TryBegin(out TaskCompletionSource<BrowserAuthResult> completion)
        {
            if (_completion != null && !_completion.Task.IsCompleted)
            {
                completion = null;
                return false;
            }

            // RunContinuationsAsynchronously: the continuation must not run
            // inside the SendMessage frame.
            _completion = new TaskCompletionSource<BrowserAuthResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            completion = _completion;
            return true;
        }

        public void ArmTimeout(TimeSpan timeout)
        {
            _timeout?.Cancel();
            _timeout?.Dispose();
            _timeout = new CancellationTokenSource();

            WaitAsync(_timeout.Token, timeout);
        }

        private async void WaitAsync(CancellationToken token, TimeSpan timeout)
        {
            try
            {
                await Task.Delay(timeout, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            Complete(BrowserAuthResult.Failed(
                XsollaIdErrorCode.Timeout, "the sign-in window did not respond in time."));
        }

        // --- called from XsollaId.jslib, do not rename ---

        public void OnAuthCallback(string payload)
        {
            try
            {
                JObject o = JObject.Parse(payload);

                string error = (string)o["error"];

                if (!string.IsNullOrEmpty(error))
                {
                    // access_denied — the player pressed "cancel" on the Xsolla side.
                    bool denied = error.IndexOf("access_denied", StringComparison.OrdinalIgnoreCase) >= 0;

                    Complete(BrowserAuthResult.Failed(
                        denied ? XsollaIdErrorCode.Cancelled : XsollaIdErrorCode.TokenExchangeFailed,
                        $"the server returned an error: {error}"));
                    return;
                }

                Complete(BrowserAuthResult.Ok((string)o["code"], (string)o["state"]));
            }
            catch (Exception ex)
            {
                Complete(BrowserAuthResult.Failed(
                    XsollaIdErrorCode.TokenExchangeFailed, $"the sign-in window response could not be parsed: {ex.Message}"));
            }
        }

        public void OnAuthCancelled() =>
            Complete(BrowserAuthResult.Failed(XsollaIdErrorCode.Cancelled, "the player closed the sign-in window."));

        public void OnPopupBlocked() =>
            Complete(BrowserAuthResult.Failed(
                XsollaIdErrorCode.PopupBlocked, "the browser blocked the sign-in window."));

        public void Complete(BrowserAuthResult result)
        {
            _timeout?.Cancel();

            // TrySetResult, not SetResult: both the answer and the window
            // closing can arrive — whichever comes first wins.
            _completion?.TrySetResult(result);
        }
    }
}
