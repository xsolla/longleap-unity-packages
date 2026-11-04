using System;
using System.Net.Http;
using System.Threading.Tasks;
using LongLeap.Core.Auth;
using UnityEngine;
using UnityEngine.Networking;

namespace LongLeap.Firebase
{
    /// <summary>
    /// Minimal Firebase Realtime Database REST client shared by the cloud save and
    /// leaderboard providers.
    /// </summary>
    /// <remarks>
    /// Builds <c>{databaseUrl}/{path}.json?auth={idToken}</c> and fetches a fresh ID token
    /// from the <see cref="IAuthProvider"/> on every request. All methods return <c>null</c>
    /// when no token is available. The token is masked in exception messages.
    /// </remarks>
    public sealed class FirebaseHttpClient
    {
        private readonly string _databaseUrl;
        private readonly IAuthProvider _auth;
        private readonly Func<string, string, string, Task<string>> _httpHandler;

        /// <param name="databaseUrl">RTDB root URL, e.g. <c>https://my-project.firebaseio.com</c>.</param>
        /// <param name="auth">Supplies the ID token attached to every request.</param>
        public FirebaseHttpClient(string databaseUrl, IAuthProvider auth)
        {
            _databaseUrl = databaseUrl?.TrimEnd('/');
            _auth = auth ?? throw new ArgumentNullException(nameof(auth));
            _httpHandler = SendUnityWebRequestAsync;
        }

        /// <param name="databaseUrl">RTDB root URL, e.g. <c>https://my-project.firebaseio.com</c>.</param>
        /// <param name="auth">Supplies the ID token attached to every request.</param>
        /// <param name="httpHandler">
        /// Replaces the network call (for tests). Receives method, URL and body; returns the
        /// response body or throws on failure.
        /// </param>
        public FirebaseHttpClient(string databaseUrl, IAuthProvider auth, Func<string, string, string, Task<string>> httpHandler)
        {
            _databaseUrl = databaseUrl?.TrimEnd('/');
            _auth = auth ?? throw new ArgumentNullException(nameof(auth));
            _httpHandler = httpHandler ?? throw new ArgumentNullException(nameof(httpHandler));
        }

        /// <summary>GET <paramref name="path"/>. Returns <c>null</c> on any failure (logged).</summary>
        /// <param name="query">Extra query string appended after the auth parameter; must start with <c>&amp;</c>.</param>
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

        /// <summary>PATCH (merge) <paramref name="body"/> into <paramref name="path"/>. Returns <c>null</c> on any failure (logged).</summary>
        /// <param name="query">Extra query string appended after the auth parameter; must start with <c>&amp;</c>.</param>
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

        /// <summary>PUT (overwrite) <paramref name="path"/> with <paramref name="body"/>. Network errors are thrown, not swallowed.</summary>
        public async Task<string> PutAsync(string path, string body)
        {
            var token = await GetTokenAsync();
            if (!token.IsValid) return null;
            return await _httpHandler("PUT", $"{_databaseUrl}/{path}.json?auth={token.Value}", body);
        }

        /// <summary>DELETE <paramref name="path"/>. Network errors are thrown, not swallowed.</summary>
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
                $"{method} {SanitizeUrl(url)} unexpected result: {request.result}");
        }

        private static string SanitizeUrl(string url)
        {
            int authIndex = url.IndexOf("auth=", StringComparison.Ordinal);
            if (authIndex < 0) return url;
            return url[..(authIndex + 5)] + "***";
        }
    }
}
