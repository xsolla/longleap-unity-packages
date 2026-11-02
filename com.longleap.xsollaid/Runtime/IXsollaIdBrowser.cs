using System;
using System.Threading.Tasks;

namespace LongLeap.XsollaId
{
    // Seam over the browser: open the authorization window and wait until the
    // callback page sends back code and state.
    //
    // Lives in the assembly that is not gated by the define, so EditMode tests
    // can plug in a fake and run without a browser, without WebGL and without
    // a registered clientId. The real implementation (jslib + popup) lives in
    // the XSOLLA_ID assembly.
    public interface IXsollaIdBrowser
    {
        // Whether a window can be opened on this platform.
        bool IsSupported { get; }

        // Opens authorizeUrl and waits for the response.
        //
        // IMPORTANT: the implementation must call window.open synchronously
        // inside the click handler — if an await happens between the click and
        // opening the window, the browser treats the window as unsolicited and
        // blocks it. That is why authorizeUrl arrives as a ready-made string:
        // building it here (let alone fetching discovery) is not allowed.
        Task<BrowserAuthResult> AuthorizeAsync(string authorizeUrl, string expectedState, TimeSpan timeout);
    }

    // What came back from the callback page.
    public readonly struct BrowserAuthResult
    {
        // One-time authorization code. Meaningful only when Error == null.
        public string Code { get; }

        // state as returned by the server. Comparing it with the expected value
        // is the caller's job: the seam does not decide what to do on mismatch.
        public string State { get; }

        // null means success.
        public XsollaIdErrorCode? Error { get; }

        public string Message { get; }

        public bool IsSuccess => Error == null;

        private BrowserAuthResult(string code, string state, XsollaIdErrorCode? error, string message)
        {
            Code = code;
            State = state;
            Error = error;
            Message = message;
        }

        public static BrowserAuthResult Ok(string code, string state) =>
            new BrowserAuthResult(code, state, null, null);

        public static BrowserAuthResult Failed(XsollaIdErrorCode code, string message) =>
            new BrowserAuthResult(null, null, code, message);
    }
}
