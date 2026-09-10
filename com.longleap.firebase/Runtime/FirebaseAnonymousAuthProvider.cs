using System;
using System.Threading;
using System.Threading.Tasks;
using LongLeap.Core.Auth;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace LongLeap.Firebase
{
    public sealed class FirebaseAnonymousAuthProvider : IAuthProvider
    {
        private readonly string _apiKey;
        private readonly Func<string, string, string, Task<string>> _httpHandler;
        private readonly SemaphoreSlim _authLock = new(1, 1);

        private string _idToken;
        private string _refreshToken;
        private string _uid;
        private long _tokenExpiresAt;

        private const long TokenRefreshMarginMs = 300000;
        private const string AuthCacheKey = "longleap_auth";

        public FirebaseAnonymousAuthProvider(string apiKey)
        {
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            _httpHandler = SendUnityWebRequestAsync;
            LoadCachedAuth();
        }

        public FirebaseAnonymousAuthProvider(string apiKey, Func<string, string, string, Task<string>> httpHandler)
        {
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            _httpHandler = httpHandler ?? throw new ArgumentNullException(nameof(httpHandler));
            LoadCachedAuth();
        }

        public async Task<AuthResult> LoginAsync()
        {
            await _authLock.WaitAsync();
            try
            {
                if (!string.IsNullOrEmpty(_idToken) && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < _tokenExpiresAt - TokenRefreshMarginMs)
                {
                    return new AuthResult.Success(new IdToken(_idToken), new UserId(_uid));
                }

                if (!string.IsNullOrEmpty(_refreshToken))
                {
                    bool refreshed = await RefreshTokenAsync();
                    if (refreshed)
                        return new AuthResult.Success(new IdToken(_idToken), new UserId(_uid));
                }

                bool signedUp = await SignUpAnonymouslyAsync();
                if (signedUp)
                    return new AuthResult.Success(new IdToken(_idToken), new UserId(_uid));

                return new AuthResult.Failure("Не удалось выполнить анонимную регистрацию в Firebase");
            }
            finally
            {
                _authLock.Release();
            }
        }

        private async Task<bool> SignUpAnonymouslyAsync()
        {
            try
            {
                string url = $"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={_apiKey}";
                string body = "{\"returnSecureToken\": true}";
                string response = await _httpHandler("POST", url, body);
                if (response == null) return false;

                var json = JObject.Parse(response);
                _idToken = json["idToken"]?.Value<string>();
                _refreshToken = json["refreshToken"]?.Value<string>();
                _uid = json["localId"]?.Value<string>();
                string expiresIn = json["expiresIn"]?.Value<string>() ?? "3600";

                if (int.TryParse(expiresIn, out int seconds))
                    _tokenExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + seconds * 1000;

                CacheAuth();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"{nameof(FirebaseAnonymousAuthProvider)}: sign-up FAILED — {ex.Message}");
                return false;
            }
        }

        private async Task<bool> RefreshTokenAsync()
        {
            try
            {
                string url = $"https://securetoken.googleapis.com/v1/token?key={_apiKey}";
                string body = $"grant_type=refresh_token&refresh_token={_refreshToken}";
                string response = await _httpHandler("POST", url, body);
                if (response == null) return false;

                var json = JObject.Parse(response);
                _idToken = json["id_token"]?.Value<string>();
                _refreshToken = json["refresh_token"]?.Value<string>() ?? _refreshToken;
                _uid = json["user_id"]?.Value<string>() ?? _uid;
                string expiresIn = json["expires_in"]?.Value<string>() ?? "3600";

                if (int.TryParse(expiresIn, out int seconds))
                    _tokenExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + seconds * 1000;

                CacheAuth();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"{nameof(FirebaseAnonymousAuthProvider)}: token refresh FAILED — {ex.Message}");
                return false;
            }
        }

        private async Task<string> SendUnityWebRequestAsync(string method, string url, string body)
        {
            using var request = new UnityWebRequest(url, method);

            if (!string.IsNullOrEmpty(body))
            {
                byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(body);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            }

            request.downloadHandler = new DownloadHandlerBuffer();

            bool isJsonBody = !string.IsNullOrEmpty(body) && body.TrimStart().StartsWith("{");
            request.SetRequestHeader("Content-Type", isJsonBody ? "application/json" : "application/x-www-form-urlencoded");

            var operation = request.SendWebRequest();
            while (!operation.isDone)
                await Task.Yield();

            if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogWarning($"{nameof(FirebaseAnonymousAuthProvider)}: HTTP {method} {url} failed: {request.error}");
                return null;
            }

            return request.downloadHandler.text;
        }

        private void CacheAuth()
        {
            var data = new JObject
            {
                ["idToken"] = _idToken,
                ["refreshToken"] = _refreshToken,
                ["uid"] = _uid,
                ["expiresAt"] = _tokenExpiresAt
            };
            PlayerPrefs.SetString(AuthCacheKey, JsonConvert.SerializeObject(data));
            PlayerPrefs.Save();
        }

        private void LoadCachedAuth()
        {
            if (!PlayerPrefs.HasKey(AuthCacheKey))
                return;

            try
            {
                string json = PlayerPrefs.GetString(AuthCacheKey);
                var data = JObject.Parse(json);
                _idToken = data["idToken"]?.Value<string>();
                _refreshToken = data["refreshToken"]?.Value<string>();
                _uid = data["uid"]?.Value<string>();
                _tokenExpiresAt = data["expiresAt"]?.Value<long>() ?? 0;
            }
            catch
            {
                PlayerPrefs.DeleteKey(AuthCacheKey);
            }
        }
    }
}
