using System;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LongLeap.XsollaId.Web.Tests
{
    [TestFixture]
    public class XsollaIdWebBrowserTests
    {
        private XsollaIdCallbackReceiver _receiver;

        // Created directly, not through Instance: Awake (DontDestroyOnLoad) does
        // not run in edit mode, and the static singleton stays untouched.
        [SetUp]
        public void SetUp() => _receiver = new GameObject("XsollaIdCallbacksTest").AddComponent<XsollaIdCallbackReceiver>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_receiver.gameObject);

        // Starts a flow and returns its task.
        private Task<BrowserAuthResult> Begin()
        {
            Assert.IsTrue(_receiver.TryBegin(out TaskCompletionSource<BrowserAuthResult> completion));
            return completion.Task;
        }

        private static BrowserAuthResult Finished(Task<BrowserAuthResult> task)
        {
            Assert.IsTrue(task.IsCompleted, "the flow must be over");
            return task.Result;
        }

        // ---- browser ----

        [Test]
        public void Authorize_InTheEditor_IsUnavailable()
        {
            var browser = new XsollaIdWebBrowser();

            Task<BrowserAuthResult> task = browser.AuthorizeAsync("https://id.xsolla.com/auth", "s", TimeSpan.FromMinutes(1));

            Assert.IsFalse(browser.IsSupported);
            Assert.AreEqual(XsollaIdErrorCode.Unavailable, Finished(task).Error);
        }

        // ---- receiver ----

        [Test]
        public void Callback_WithCodeAndState_IsOk()
        {
            Task<BrowserAuthResult> task = Begin();

            _receiver.OnAuthCallback("{\"code\":\"code-1\",\"state\":\"state-1\"}");

            BrowserAuthResult result = Finished(task);
            Assert.IsTrue(result.IsSuccess, result.Message);
            Assert.AreEqual("code-1", result.Code);
            Assert.AreEqual("state-1", result.State);
        }

        [TestCase("{\"error\":\"access_denied\"}", XsollaIdErrorCode.Cancelled)]
        [TestCase("{\"error\":\"ACCESS_DENIED\",\"code\":\"c\"}", XsollaIdErrorCode.Cancelled)]
        [TestCase("{\"error\":\"server_error\"}", XsollaIdErrorCode.TokenExchangeFailed)]
        [TestCase("not json", XsollaIdErrorCode.TokenExchangeFailed)]
        [TestCase("", XsollaIdErrorCode.TokenExchangeFailed)]
        public void Callback_Failure_IsClassified(string payload, XsollaIdErrorCode expected)
        {
            Task<BrowserAuthResult> task = Begin();

            _receiver.OnAuthCallback(payload);

            BrowserAuthResult result = Finished(task);
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(expected, result.Error);
            Assert.IsNull(result.Code, "a failure carries no code");
        }

        [Test]
        public void AuthCancelled_IsCancelled()
        {
            Task<BrowserAuthResult> task = Begin();

            _receiver.OnAuthCancelled();

            Assert.AreEqual(XsollaIdErrorCode.Cancelled, Finished(task).Error);
        }

        [Test]
        public void PopupBlocked_IsPopupBlocked()
        {
            Task<BrowserAuthResult> task = Begin();

            _receiver.OnPopupBlocked();

            Assert.AreEqual(XsollaIdErrorCode.PopupBlocked, Finished(task).Error);
        }

        // One receiver for the whole game: a second flow would mix up the answers.
        [Test]
        public void TryBegin_AllowsOneFlowAtATime()
        {
            Begin();

            Assert.IsFalse(_receiver.TryBegin(out TaskCompletionSource<BrowserAuthResult> second));
            Assert.IsNull(second);
        }

        [Test]
        public void TryBegin_AfterCompletion_StartsAFreshFlow()
        {
            Task<BrowserAuthResult> first = Begin();
            _receiver.OnAuthCancelled();

            Task<BrowserAuthResult> second = Begin();

            Assert.AreNotSame(first, second);
            Assert.IsFalse(second.IsCompleted, "the old answer must not leak into the new flow");
        }

        // Both the answer and the window closing can arrive: the first one wins.
        [Test]
        public void Complete_FirstAnswerWins()
        {
            Task<BrowserAuthResult> task = Begin();

            _receiver.OnAuthCallback("{\"code\":\"code-1\",\"state\":\"state-1\"}");
            _receiver.OnAuthCancelled();

            Assert.IsTrue(Finished(task).IsSuccess);
        }

        [Test]
        public void Complete_WithoutAFlow_IsIgnored()
        {
            Assert.DoesNotThrow(() => _receiver.OnAuthCancelled());
        }
    }
}
