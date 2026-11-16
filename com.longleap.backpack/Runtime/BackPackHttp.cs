using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace LongLeap.Backpack
{
    // What is sent. A separate struct rather than a long argument list: a fake
    // transport in tests sees the whole request, as in XsollaIdHttp.
    public readonly struct BackPackHttpCall
    {
        public const string Get = "GET";
        public const string Post = "POST";
        public const string Delete = "DELETE";

        // The header the server reads the player's token from (UserTokenHeader in the
        // reward server, USER_TOKEN_HEADER in backpack_service).
        public const string UserTokenHeader = "X-User-Token";

        public string Method { get; }
        public string Url { get; }

        // A JSON body, or null when there is none (GET, DELETE).
        public string Body { get; }

        public int TimeoutSeconds { get; }

        // The signed-in player's Xsolla ID token, sent in UserTokenHeader; null for a guest.
        public string UserToken { get; }

        public BackPackHttpCall(string method, string url, string body, int timeoutSeconds,
            string userToken = null)
        {
            Method = method;
            Url = url;
            Body = body;
            TimeoutSeconds = timeoutSeconds;
            UserToken = string.IsNullOrEmpty(userToken) ? null : userToken;
        }

        // The token is never printed: whoever holds it is the player (see AccessToken).
        public override string ToString() => $"{Method} {Url}";
    }

    // A raw reply: one of three.
    //  - A status code with a body — the server answered.
    //  - TransportError — the request was sent but there is no answer (network,
    //    timeout, abort). The same split as in XsollaIdHttp: "the server said 400"
    //    and "no network" lead to different decisions — retrying makes sense only
    //    in the second case.
    //  - NotSentReason — the request was never sent: the service is not
    //    configured, there is no xsollaID, or the service is already stopped.
    //    Retrying will not help until the config or sign-in changes — so this is
    //    not a transport error.
    public readonly struct BackPackHttpReply
    {
        public long StatusCode { get; }
        public string Body { get; }
        public string TransportError { get; }
        public string NotSentReason { get; }

        public bool IsTransportError => TransportError != null;
        public bool IsNotSent => NotSentReason != null;
        public bool IsHttpSuccess => !IsTransportError && !IsNotSent && StatusCode >= 200 && StatusCode < 300;

        public BackPackHttpReply(long statusCode, string body, string transportError)
            : this(statusCode, body, transportError, null)
        {
        }

        private BackPackHttpReply(long statusCode, string body, string transportError, string notSentReason)
        {
            StatusCode = statusCode;
            Body = body;
            TransportError = transportError;
            NotSentReason = notSentReason;
        }

        public static BackPackHttpReply Transport(string error) => new BackPackHttpReply(0, null, error);
        public static BackPackHttpReply NotSent(string reason) => new BackPackHttpReply(0, null, null, reason);
        public static BackPackHttpReply Ok(string body) => new BackPackHttpReply(200, body, null);
        public static BackPackHttpReply Status(long code, string body = null) => new BackPackHttpReply(code, body, null);

        public override string ToString() =>
            IsNotSent ? $"not sent: {NotSentReason}"
            : IsTransportError ? $"transport: {TransportError}"
            : $"http_{StatusCode}: {Body}";
    }

    // The single point through which the package goes to the network. Tests
    // replace it, so response handling is checked without a server.
    //
    // Contract: a network failure is BackPackHttpReply.Transport, not an
    // exception. Cancellation is OperationCanceledException. Any other exception
    // is turned into a transport error by BackPackClient anyway.
    public interface IBackPackTransport
    {
        Task<BackPackHttpReply> SendAsync(BackPackHttpCall call, CancellationToken cancellationToken);
    }

    // The default transport. Same shape as XsollaIdHttp: UnityWebRequest and
    // waiting via while (!op.isDone) await Task.Yield() — WebGL has no threads,
    // so this is the only option that works.
    //
    // Cancellation is checked on every frame of the wait: Abort drops the
    // request, and an answer arriving after cancellation is never parsed.
    public sealed class UnityWebRequestBackPackTransport : IBackPackTransport
    {
        public async Task<BackPackHttpReply> SendAsync(BackPackHttpCall call, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var request = new UnityWebRequest(call.Url, call.Method);
            request.timeout = call.TimeoutSeconds;

            // A UnityWebRequest built by the constructor or by Delete comes WITHOUT a
            // downloadHandler — without one the response body cannot be read, and the
            // server's report would be lost.
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Accept", "application/json");

            if (call.UserToken != null)
                request.SetRequestHeader(BackPackHttpCall.UserTokenHeader, call.UserToken);

            if (call.Body != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(call.Body));
                request.SetRequestHeader("Content-Type", "application/json");
            }

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();

            while (!operation.isDone)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    request.Abort();
                    cancellationToken.ThrowIfCancellationRequested();
                }

                await Task.Yield();
            }

            if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.DataProcessingError)
            {
                // The network failed — there is no status code at all.
                return BackPackHttpReply.Transport($"{call} — {request.error}");
            }

            return new BackPackHttpReply(request.responseCode, request.downloadHandler?.text, null);
        }
    }
}
