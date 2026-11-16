namespace LongLeap.XsollaId
{
    // Result of a sign-in operation. A closed union of the same shape as
    // LongLeap.Core.Auth.AuthResult: private constructor + nested sealed
    // subclasses, so callers match with `result is
    // XsollaIdResult.Success success`.
    //
    // IXsollaIdAuth NEVER lets exceptions out. Any failure arrives here as
    // Failure: an exception surfacing in a Task that is polled from a
    // coroutine means a hung bootstrap.
    public abstract class XsollaIdResult
    {
        private XsollaIdResult() { }

        public sealed class Success : XsollaIdResult
        {
            public XsollaIdSession Session { get; }

            public Success(XsollaIdSession session) => Session = session;
        }

        public sealed class Failure : XsollaIdResult
        {
            public XsollaIdErrorCode Code { get; }

            // For the log, not for the player: the player sees a localized
            // string chosen by Code. Never contains tokens.
            public string Message { get; }

            public Failure(XsollaIdErrorCode code, string message)
            {
                Code = code;
                Message = message;
            }
        }

        // Shorthand factories for the most common outcomes, so the same
        // messages are not repeated across the code.
        public static XsollaIdResult Cancelled() =>
            new Failure(XsollaIdErrorCode.Cancelled, "The player closed the sign-in window.");

        public static XsollaIdResult Unavailable() =>
            new Failure(XsollaIdErrorCode.Unavailable, "Xsolla ID sign-in is not available in this build.");

        public static XsollaIdResult Expired() =>
            new Failure(XsollaIdErrorCode.SessionExpired, "There is no saved session, or it is no longer valid.");
    }
}
