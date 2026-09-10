using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace LongLeap.XsollaId.Web
{
    // Открывает окно входа и принимает ответ из JS.
    //
    // Мост односторонний в обе стороны по отдельности: C# → JS через
    // [DllImport("__Internal")], JS → C# через SendMessage в объект-приёмник.
    // Такой же приём использовал вендорский SDK Xsolla; своего повода
    // изобретать другой нет.
    public sealed class XsollaIdWebBrowser : IXsollaIdBrowser
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void OpenXsollaIdPopup(string url);
#else
        // В редакторе и на других платформах окна нет: IsSupported вернёт
        // false и до вызова дело не дойдёт. Заглушка нужна только для
        // компиляции.
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

            // Один вход за раз. XsollaIdAuth уже отдаёт ту же задачу на
            // повторный клик, но приёмник один на всю игру, и второй
            // одновременный вход перепутал бы ответы.
            if (!receiver.TryBegin(out TaskCompletionSource<BrowserAuthResult> completion))
            {
                return Task.FromResult(BrowserAuthResult.Failed(
                    XsollaIdErrorCode.Cancelled, "sign-in is already in progress."));
            }

            receiver.ArmTimeout(timeout);

            try
            {
                // Синхронно и последним действием: жест игрока ещё «горячий».
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

    // Приёмник сообщений из JS. Живёт в DontDestroyOnLoad, потому что окно
    // входа переживает перезагрузку сцены (рестарт после смерти), а ответ
    // должен дойти в любом случае.
    internal sealed class XsollaIdCallbackReceiver : MonoBehaviour
    {
        // Имя объекта, по которому SendMessage из XsollaId.jslib его находит.
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
                    // Имя фиксированное: по нему SendMessage из jslib находит
                    // объект. Менять только вместе с XsollaId.jslib.
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

            // RunContinuationsAsynchronously: продолжение не должно
            // выполняться внутри кадра SendMessage.
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

        // --- вызывается из XsollaId.jslib, имена не менять ---

        public void OnAuthCallback(string payload)
        {
            try
            {
                JObject o = JObject.Parse(payload);

                string error = (string)o["error"];

                if (!string.IsNullOrEmpty(error))
                {
                    // access_denied — игрок нажал «отмена» на стороне Xsolla.
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

            // TrySetResult, а не SetResult: приехать может и ответ, и закрытие
            // окна — кто первый, тот и прав.
            _completion?.TrySetResult(result);
        }
    }
}
