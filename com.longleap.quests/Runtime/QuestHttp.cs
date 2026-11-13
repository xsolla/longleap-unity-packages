using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace LongLeap.Quests
{
    // A raw reply: either a status code with a body, or a transport error.
    public readonly struct QuestHttpReply
    {
        public long StatusCode { get; }
        public string Body { get; }

        // Not null — the request went out but no answer came (network, timeout, abort).
        public string TransportError { get; }

        public bool IsTransportError => TransportError != null;

        public bool IsHttpSuccess => StatusCode >= 200 && StatusCode < 300;

        public QuestHttpReply(long statusCode, string body, string transportError)
        {
            StatusCode = statusCode;
            Body = body;
            TransportError = transportError;
        }
    }

    // How a request reaches the server. Replaced by tests.
    public interface IQuestTransport
    {
        Task<QuestHttpReply> SendAsync(
            string method, string url, string body,
            IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken);
    }

    // The default transport: UnityWebRequest, waited on with Task.Yield — WebGL
    // has no threads, so this is the only option that works there.
    public sealed class UnityWebRequestQuestTransport : IQuestTransport
    {
        public const int TimeoutSeconds = 10;

        public async Task<QuestHttpReply> SendAsync(
            string method, string url, string body,
            IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken)
        {
            using var request = new UnityWebRequest(url, method);
            request.timeout = TimeoutSeconds;

            // Without a downloadHandler the body — where the refusal reason is — is lost.
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Accept", "application/json");

            if (body != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                request.SetRequestHeader("Content-Type", "application/json");
            }

            if (headers != null)
            {
                foreach (var header in headers)
                    request.SetRequestHeader(header.Key, header.Value);
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

            // ProtocolError is a 4xx/5xx answer — the body is still there.
            switch (request.result)
            {
                case UnityWebRequest.Result.Success:
                case UnityWebRequest.Result.ProtocolError:
                    return new QuestHttpReply(request.responseCode, request.downloadHandler.text, null);

                default:
                    return new QuestHttpReply(0, null,
                        string.IsNullOrEmpty(request.error) ? request.result.ToString() : request.error);
            }
        }
    }
}
