using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Core.CloudSaves;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LongLeap.XsollaId.Accounts.Tests
{
    [TestFixture]
    public class AccountGateTests
    {
        private sealed class FakeAuth : IXsollaIdAuth
        {
            public bool IsAvailable { get; set; } = true;
            public bool IsReady { get; set; } = true;
            public bool IsSignedIn => Current != null;
            public XsollaIdSignInOrigin LastSignInOrigin => XsollaIdSignInOrigin.Interactive;
            public XsollaIdSession Current { get; set; }
            public Func<Task<XsollaIdResult>> SignIn { get; set; }

            public event Action<XsollaIdSession> SignedIn { add { } remove { } }
            public event Action SignedOut { add { } remove { } }
            public event Action<XsollaIdSession> SessionUpdated { add { } remove { } }

            public Task<XsollaIdResult> SignInAsync() => SignIn();
            public Task<XsollaIdResult> RestoreAsync() => throw new NotSupportedException();
            public Task<XsollaIdResult> RefreshAsync() => throw new NotSupportedException();
            public Task SignOutAsync() => Task.CompletedTask;
        }

        private sealed class FakeScreen : IAccountScreen
        {
            public readonly List<XsollaIdErrorCode?> Choices = new List<XsollaIdErrorCode?>();
            public bool Closed;

            public Action SignInClicked { get; set; }
            public Action GuestClicked { get; set; }
            public Action RetryClicked { get; set; }

            public void ShowChoice(XsollaIdErrorCode? lastError) => Choices.Add(lastError);
            public void ShowBusy(AccountScreenBusy reason) { }
            public void ShowServerError() { }
            public void Close() => Closed = true;
        }

        private sealed class FakeScope : IAccountCloudSaveScope
        {
            public bool IsAccountScoped => !string.IsNullOrEmpty(AccountId);
            public string AccountId { get; private set; } = "";
            public void UseAccount(string accountId) => AccountId = accountId;
            public void UseGuest() => AccountId = "";

            public Task<IReadOnlyDictionary<string, CloudSaveEntry>> PeekAccountAsync(string accountId) =>
                throw new NotSupportedException();
        }

        private static Task<IReadOnlyDictionary<string, CloudSaveEntry>> EmptySave(string accountId) =>
            Task.FromResult<IReadOnlyDictionary<string, CloudSaveEntry>>(new Dictionary<string, CloudSaveEntry>());

        private static XsollaIdSession Session(string subject) =>
            new XsollaIdSession(new XsollaUserId(subject), null, new AccessToken("access"), new RefreshToken("refresh"), 0);

        private FakeAuth _auth;
        private FakeScreen _screen;
        private int _screensCreated;

        [SetUp]
        public void SetUp()
        {
            AccountGate.ForgetGuestChoice();
            _auth = new FakeAuth();
            _screen = new FakeScreen();
            _screensCreated = 0;
        }

        [TearDown]
        public void TearDown() => AccountGate.ForgetGuestChoice();

        private AccountGate Gate() => new AccountGate(_auth, new FakeScope(), EmptySave, () =>
        {
            _screensCreated++;
            return _screen;
        });

        [Test]
        public void SignInUnavailable_IsGuestWithoutScreen()
        {
            _auth.IsAvailable = false;

            Task<AccountGateResult> run = Gate().RunAsync();

            Assert.IsTrue(run.IsCompleted);
            Assert.IsTrue(run.Result.IsGuest);
            Assert.AreEqual(0, _screensCreated);
        }

        [Test]
        public void RestoredSession_PlaysTheAccountWithoutScreen()
        {
            _auth.Current = Session("acc-1");

            Task<AccountGateResult> run = Gate().RunAsync();

            Assert.IsTrue(run.IsCompleted, "nothing to wait for: the session and the save are ready");
            Assert.AreEqual("acc-1", run.Result.AccountId);
            Assert.IsNotNull(run.Result.AccountEntries);
            Assert.AreEqual(0, _screensCreated);
        }

        [UnityTest]
        public IEnumerator SignInThrows_ShowsAnErrorAndGuestStillWorks()
        {
            _auth.SignIn = () => throw new InvalidOperationException("boom");

            Task<AccountGateResult> run = Gate().RunAsync();

            yield return WaitUntil(() => _screen.Choices.Count == 1);
            Assert.IsNull(_screen.Choices[0], "the first choice has no error");

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("sign-in failed"));
            _screen.SignInClicked();

            yield return WaitUntil(() => _screen.Choices.Count == 2);
            Assert.AreEqual(XsollaIdErrorCode.InvalidState, _screen.Choices[1]);

            _screen.GuestClicked();

            yield return WaitUntil(() => run.IsCompleted);
            Assert.IsTrue(run.Result.IsGuest);
            Assert.IsTrue(_screen.Closed);
        }

        // The gate resumes through Task.Yield, so let the editor run frames.
        private static IEnumerator WaitUntil(Func<bool> condition, int maxFrames = 300)
        {
            for (int i = 0; i < maxFrames && !condition(); i++)
                yield return null;

            Assert.IsTrue(condition(), "timed out waiting for the gate");
        }
    }
}
