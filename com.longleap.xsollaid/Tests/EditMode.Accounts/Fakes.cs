using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Core.CloudSaves;

namespace LongLeap.XsollaId.Accounts.Tests
{
    internal static class TestSaves
    {
        public static Dictionary<string, CloudSaveEntry> Save(params (string key, string value)[] pairs)
        {
            var save = new Dictionary<string, CloudSaveEntry>();

            foreach (var (key, value) in pairs)
                save[key] = new CloudSaveEntry(value, 100);

            return save;
        }
    }

    // Whose save sits in the local mirror; starts as guest.
    internal sealed class FakeScope : IAccountCloudSaveScope
    {
        public bool IsAccountScoped => !string.IsNullOrEmpty(AccountId);
        public string AccountId { get; private set; } = "";
        public void UseAccount(string accountId) => AccountId = accountId;
        public void UseGuest() => AccountId = "";

        // Required by the interface; the gate and the switch don't call it.
        public Task<IReadOnlyDictionary<string, CloudSaveEntry>> PeekAccountAsync(string accountId) =>
            Task.FromResult<IReadOnlyDictionary<string, CloudSaveEntry>>(new Dictionary<string, CloudSaveEntry>());
    }

    internal sealed class FakeAuth : IXsollaIdAuth
    {
        public bool IsAvailable { get; set; } = true;
        public bool IsReady { get; set; } = true;
        public bool IsSignedIn => Current != null;
        public XsollaIdSignInOrigin LastSignInOrigin => XsollaIdSignInOrigin.Interactive;
        public XsollaIdSession Current { get; set; }

        // What SignInAsync does; the player closing the window by default.
        public Func<Task<XsollaIdResult>> SignIn { get; set; } =
            () => Task.FromResult(XsollaIdResult.Cancelled());

        public int SignInCalls { get; private set; }

        public event Action<XsollaIdSession> SignedIn { add { } remove { } }
        public event Action SignedOut { add { } remove { } }
        public event Action<XsollaIdSession> SessionUpdated { add { } remove { } }

        public void SignInReturns(XsollaIdResult result) => SignIn = () => Task.FromResult(result);

        public void SignInThrows(Exception exception) => SignIn = () => throw exception;

        public Task<XsollaIdResult> SignInAsync()
        {
            SignInCalls++;
            return SignIn();
        }

        public Task<XsollaIdResult> RestoreAsync() => throw new NotSupportedException();
        public Task<XsollaIdResult> RefreshAsync() => throw new NotSupportedException();
        public Task SignOutAsync() => Task.CompletedTask;
    }

    // Records every call, so a test can tell which path the gate took.
    internal sealed class FakeScreen : IAccountScreen
    {
        public readonly List<XsollaIdErrorCode?> Choices = new List<XsollaIdErrorCode?>();
        public readonly List<AccountScreenBusy> Busy = new List<AccountScreenBusy>();
        public int ServerErrors;
        public int Closes;

        public Action SignInClicked { get; set; }
        public Action GuestClicked { get; set; }
        public Action RetryClicked { get; set; }

        public void ShowChoice(XsollaIdErrorCode? lastError) => Choices.Add(lastError);
        public void ShowBusy(AccountScreenBusy reason) => Busy.Add(reason);
        public void ShowServerError() => ServerErrors++;
        public void Close() => Closes++;
    }
}
