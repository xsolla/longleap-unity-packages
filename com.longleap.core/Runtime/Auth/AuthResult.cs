namespace LongLeap.Core.Auth
{
    public abstract class AuthResult
    {
        private AuthResult() { }

        public sealed class Success : AuthResult
        {
            public IdToken Token { get; }
            public UserId Uid { get; }
            public Success(IdToken token, UserId uid)
            {
                Token = token;
                Uid = uid;
            }
        }

        public sealed class Failure : AuthResult
        {
            public string Error { get; }
            public Failure(string error) => Error = error;
        }
    }
}
