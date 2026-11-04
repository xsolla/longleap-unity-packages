using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace LongLeap.Analytics
{
    // Default transport: one UnityWebRequest per POST. Works on every
    // platform, but on WebGL a request still running when the tab closes is
    // lost; use WebGlBeaconTransport there.
    public sealed class UnityWebRequestAnalyticsTransport : IAnalyticsTransport
    {
        // GA4 parses the body whatever the content type. text/plain keeps the
        // request "simple" for browsers, so WebGL builds send it without a
        // CORS preflight.
        private const string ContentType = "text/plain;charset=UTF-8";

        private readonly int _timeoutSeconds;

        public UnityWebRequestAnalyticsTransport(int timeoutSeconds = 15)
        {
            if (timeoutSeconds < 1)
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), timeoutSeconds, "Must be at least 1.");

            _timeoutSeconds = timeoutSeconds;
        }

        public Task<AnalyticsHttpResponse> PostAsync(string url, string body)
        {
            UnityWebRequest request = null;
            try
            {
                request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
                {
                    uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body ?? string.Empty)),
                    downloadHandler = new DownloadHandlerBuffer(),
                    timeout = _timeoutSeconds,
                };
                request.SetRequestHeader("Content-Type", ContentType);

                var completion = new TaskCompletionSource<AnalyticsHttpResponse>();
                var sent = request;

                // Invoked on the main thread; immediately if already done.
                request.SendWebRequest().completed += _ =>
                {
                    try
                    {
                        completion.TrySetResult(ToResponse(sent));
                    }
                    finally
                    {
                        sent.Dispose();
                    }
                };

                return completion.Task;
            }
            catch (Exception ex)
            {
                request?.Dispose();
                return Task.FromResult(AnalyticsHttpResponse.NetworkError(ex.Message));
            }
        }

        private static AnalyticsHttpResponse ToResponse(UnityWebRequest request)
        {
            if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.DataProcessingError)
            {
                return AnalyticsHttpResponse.NetworkError(request.error);
            }

            var error = request.result == UnityWebRequest.Result.Success ? null : request.error;
            return new AnalyticsHttpResponse(request.responseCode, request.downloadHandler?.text, error);
        }
    }
}
