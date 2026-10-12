using System;
using System.Threading.Tasks;
using LongLeap.Core.Auth;

namespace LongLeap.XsollaId.Web
{
    // Hands LeaderboardService the Xsolla ID account as its uid, in place of
    // the Firebase anonymous uid it used before.
    //
    // LeaderboardService submits and looks the player up under the uid from
    // IAuthProvider, and LeaderboardScreen finds "you" in the list by it. With
    // XsollaLeaderboardProvider the player's own row comes back with the
    // account id as uid, so both sides must use the same value — this one.
    //
    // No token: the server identifies the player by xsollaId alone.
    public sealed class XsollaAccountAuthProvider : IAuthProvider
    {
        private readonly string _accountId;

        public XsollaAccountAuthProvider(string accountId)
        {
            if (string.IsNullOrWhiteSpace(accountId))
                throw new ArgumentException($"{nameof(XsollaAccountAuthProvider)}: accountId is empty.", nameof(accountId));

            _accountId = accountId;
        }

        public Task<AuthResult> LoginAsync() =>
            Task.FromResult<AuthResult>(new AuthResult.Success(IdToken.None, new UserId(_accountId)));
    }
}
