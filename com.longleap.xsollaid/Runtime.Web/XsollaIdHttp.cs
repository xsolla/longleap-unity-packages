using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace LongLeap.XsollaId.Web
{
    // Что отправляем. Отдельная структура, а не длинный список аргументов:
    // так подделка в тестах подменяет один делегат и видит всё сразу.
    public readonly struct HttpCall
    {
        public string Method { get; }
        public string Url { get; }
        public string Body { get; }
        public string ContentType { get; }
        public string BearerToken { get; }

        // Timeout for this request in seconds; 0 means the shared default. Large
        // save batches on a slow network don't fit into the default 10 s.
        public int TimeoutSeconds { get; }

        public HttpCall(string method, string url, string body = null, string contentType = null, string bearerToken = null,
            int timeoutSeconds = 0)
        {
            Method = method;
            Url = url;
            Body = body;
            ContentType = contentType;
            BearerToken = bearerToken;
            TimeoutSeconds = timeoutSeconds;
        }
    }

    // Ответ: либо код состояния с телом, либо транспортная ошибка (запрос
    // вообще не дошёл). Различать важно: 400 от сервера и обрыв сети ведут к
    // разным кодам XsollaIdErrorCode и к разному поведению UI.
    public readonly struct HttpReply
    {
        public long StatusCode { get; }
        public string Body { get; }
        public string TransportError { get; }

        public bool IsTransportError => TransportError != null;
        public bool IsSuccess => !IsTransportError && StatusCode >= 200 && StatusCode < 300;

        public HttpReply(long statusCode, string body, string transportError)
        {
            StatusCode = statusCode;
            Body = body;
            TransportError = transportError;
        }

        public static HttpReply Transport(string error) => new HttpReply(0, null, error);
        public static HttpReply Ok(string body) => new HttpReply(200, body, null);
        public static HttpReply Status(long code, string body = null) => new HttpReply(code, body, null);
    }

    // Транспорт для запросов к Xsolla ID. Форма и повадки скопированы с
    // FirebaseHttpClient, чтобы в проекте был один способ ходить по сети:
    // UnityWebRequest, ожидание через `while (!op.isDone) await Task.Yield()`
    // (единственный безопасный вариант под WebGL — потоков там нет),
    // request.timeout = 10 и вычищенные от секретов сообщения об ошибках.
    public sealed class XsollaIdHttp
    {
        private const int TimeoutSeconds = 10;

        private readonly Func<HttpCall, Task<HttpReply>> _handler;

        public XsollaIdHttp(Func<HttpCall, Task<HttpReply>> handler = null)
        {
            _handler = handler ?? SendAsync;
        }

        public Task<HttpReply> GetAsync(string url, string bearerToken = null) =>
            _handler(new HttpCall("GET", url, bearerToken: bearerToken));

        public Task<HttpReply> PostFormAsync(
            string url, IReadOnlyDictionary<string, string> form, string bearerToken = null) =>
            _handler(new HttpCall(
                "POST", url, EncodeForm(form), "application/x-www-form-urlencoded", bearerToken));

        // An arbitrary request: cloud saves need PUT and DELETE with a JSON body,
        // and a second transport just for them is not worth it.
        public Task<HttpReply> RequestAsync(HttpCall call) => _handler(call);

        public static string EncodeForm(IReadOnlyDictionary<string, string> form)
        {
            var sb = new StringBuilder();

            foreach (KeyValuePair<string, string> pair in form)
            {
                if (string.IsNullOrEmpty(pair.Value))
                    continue;

                if (sb.Length > 0)
                    sb.Append('&');

                sb.Append(Uri.EscapeDataString(pair.Key)).Append('=').Append(Uri.EscapeDataString(pair.Value));
            }

            return sb.ToString();
        }

        private static async Task<HttpReply> SendAsync(HttpCall call)
        {
            using var request = new UnityWebRequest(call.Url, call.Method);
            request.timeout = call.TimeoutSeconds > 0 ? call.TimeoutSeconds : TimeoutSeconds;
            request.downloadHandler = new DownloadHandlerBuffer();

            if (!string.IsNullOrEmpty(call.Body))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(call.Body));
                request.SetRequestHeader("Content-Type", call.ContentType ?? "application/json");
            }

            if (!string.IsNullOrEmpty(call.BearerToken))
                request.SetRequestHeader("Authorization", "Bearer " + call.BearerToken);

            request.SetRequestHeader("Accept", "application/json");

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();

            while (!operation.isDone)
                await Task.Yield();

            if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.DataProcessingError)
            {
                // Сети не хватило — кода состояния нет вовсе.
                return HttpReply.Transport($"{call.Method} {Sanitize(call.Url)} — {request.error}");
            }

            return new HttpReply(request.responseCode, request.downloadHandler?.text, null);
        }

        // Секретов в url'ах Xsolla ID нет, но code и state в логах ни к чему:
        // code одноразовый, а state — часть защиты от CSRF.
        public static string Sanitize(string url)
        {
            if (string.IsNullOrEmpty(url))
                return url;

            int query = url.IndexOf('?');

            return query < 0 ? url : url.Substring(0, query) + "?…";
        }

        internal static void LogWarning(string message) => Debug.LogWarning($"XsollaId: {message}");
    }
}
