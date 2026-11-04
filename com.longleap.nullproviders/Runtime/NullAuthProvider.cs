using System;
using System.Threading.Tasks;
using LongLeap.Core.Auth;

namespace LongLeap.Null
{
    /// <summary>
    /// Auth backend without a server: login always succeeds with no token
    /// (<see cref="IdToken.None"/>) and a fixed user id.
    /// </summary>
    /// <remarks>
    /// Every instance created with the default id reports the same user, so
    /// all null-auth players share one identity (for example, one leaderboard
    /// entry). Pass a per-device id when that matters.
    /// </remarks>
    public sealed class NullAuthProvider : IAuthProvider
    {
        public const string DefaultUserId = "anonymous";

        private readonly Task<AuthResult> _result;

        /// <param name="userId">Id reported on login. Must not be null or empty.</param>
        /// <exception cref="ArgumentNullException"><paramref name="userId"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="userId"/> is empty.</exception>
        public NullAuthProvider(string userId = DefaultUserId)
        {
            if (userId == null)
                throw new ArgumentNullException(nameof(userId));

            if (userId.Length == 0)
                throw new ArgumentException("User id must not be empty.", nameof(userId));

            _result = Task.FromResult<AuthResult>(new AuthResult.Success(IdToken.None, new UserId(userId)));
        }

        public Task<AuthResult> LoginAsync() => _result;
    }
}
