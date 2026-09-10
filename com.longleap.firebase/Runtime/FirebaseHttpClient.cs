using System;
using System.Net.Http;
using System.Threading.Tasks;
using LongLeap.Core.Auth;
using UnityEngine;
using UnityEngine.Networking;

namespace LongLeap.Firebase
{
    public sealed class FirebaseHttpClient
    {
        private readonly string _databaseUrl;
        private readonly IAuthProvider _auth;
        private readonly Func<string, string, string, Task<string>> _httpHandler;

        public FirebaseHttpClient(string databaseUrl, IAuthProvider auth)
        {
            _databaseUrl = databaseUrl?.TrimEnd('/');
            _auth = auth ?? throw new ArgumentNullException(nameof(auth));
            _httpHandler = SendUnityWebRequestAsync;
        }

        public FirebaseHttpClient(string databaseUrl, IAuthProvider auth, Func<string, string, string, Task<string>> httpHandler)
        {
            _databaseUrl = databaseUrl?.TrimEnd('/');
            _auth = auth ?? throw new ArgumentNullException(nameof(auth));
            _httpHandler = httpHandler ?? throw new ArgumentNullException(nameof(httpHandler));
        }

        public async Task<string> GetAsync(string path, string query = null)
        {
            var token = await GetTokenAsync();
            if (!token.IsValid) return null;
            try
            {
                return await _httpHandler("GET", BuildUrl(path, token.Value, query), null);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{nameof(FirebaseHttpClient)}: GET {path} FAILED — {ex.Message}");
                return null;
            }
        }

        public async Task<string> PatchAsync(string path, string body, string query = null)
        {
            var token = await GetTokenAsync();
            if (!token.IsValid) return null;
            try
            {
                return await _httpHandler("PATCH", BuildUrl(path, token.Value, query), body);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{nameof(FirebaseHttpClient)}: PATCH {path} FAILED — {ex.Message}");
                return null;
            }
        }

        public async Task<string> PutAsync(string path, string body)
        {
            var token = await GetTokenAsync();
            if (!token.IsValid) return null;
            return await _httpHandler("PUT", $"{_databaseUrl}/{path}.json?auth={token.Value}", body);
        }

        public async Task<string> DeleteAsync(string path)
        {
            var token = await GetTokenAsync();
            if (!token.IsValid) return null;
            return await _httpHandler("DELETE", $"{_databaseUrl}/{path}.json?auth={token.Value}", null);
        }

        private string BuildUrl(string path, string token, string query)
        {
            string url = $"{_databaseUrl}/{path}.json?auth={token}";
            if (!string.IsNullOrEmpty(query))
                url += query;
            return url;
        }

        private async Task<IdToken> GetTokenAsync()
        {
            var result = await _auth.LoginAsync();
            if (result is AuthResult.Success success)
                return success.Token;
            return IdToken.None;
        }

        private async Task<string> SendUnityWebRequestAsync(string method, string url, string body)
        {
            using var request = new UnityWebRequest(url, method);
            request.timeout = 10;

            if (!string.IsNullOrEmpty(body))
            {
                byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(body);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            }

            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            var operation = request.SendWebRequest();
            while (!operation.isDone)
                await Task.Yield();

            if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.ProtocolError ||
                request.result == UnityWebRequest.Result.DataProcessingError)
            {
                throw new HttpRequestException(
                    $"{method} {SanitizeUrl(url)} FAILED — {request.error} (code {request.responseCode})");
            }

            if (request.result == UnityWebRequest.Result.Success)
                return request.downloadHandler.text;

            throw new HttpRequestException(
                $"{method} {SanitizeUrl(url)} неожиданный результат: {request.result}");
        }

        private static string SanitizeUrl(string url)
        {
            int authIndex = url.IndexOf("auth=", StringComparison.Ordinal);
            if (authIndex < 0) return url;
            return url[..(authIndex + 5)] + "***";
        }
    }
}
