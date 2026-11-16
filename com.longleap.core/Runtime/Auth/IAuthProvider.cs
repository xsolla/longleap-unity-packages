using System.Threading.Tasks;

namespace LongLeap.Core.Auth
{
    public interface IAuthProvider
    {
        Task<AuthResult> LoginAsync();
    }
}
