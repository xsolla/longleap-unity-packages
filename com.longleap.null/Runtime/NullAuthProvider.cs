using System.Threading.Tasks;
using LongLeap.Core.Auth;

namespace LongLeap.Null
{
    public sealed class NullAuthProvider : IAuthProvider
    {
        public Task<AuthResult> LoginAsync()
        {
            return Task.FromResult<AuthResult>(new AuthResult.Success(IdToken.None, new UserId("anonymous")));
        }
    }
}
