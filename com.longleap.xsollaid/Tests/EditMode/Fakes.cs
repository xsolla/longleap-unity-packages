using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Core;
using LongLeap.XsollaId;

namespace LongLeap.XsollaId.Tests
{
    // Подделки пишем руками: библиотеки моков в проекте нет, и остальные
    // пакеты тестируются так же.

    internal sealed class FakePlayerPrefs : IPlayerPrefs
    {
        public readonly Dictionary<string, string> Strings = new Dictionary<string, string>();
        public int SaveCalls;

        public string GetString(string key, string defaultValue = "") =>
            Strings.TryGetValue(key, out string v) ? v : defaultValue;

        public int GetInt(string key, int defaultValue = 0) => defaultValue;
        public float GetFloat(string key, float defaultValue = 0f) => defaultValue;

        public void SetString(string key, string value) => Strings[key] = value;
        public void SetInt(string key, int value) { }
        public void SetFloat(string key, float value) { }

        public bool HasKey(string key) => Strings.ContainsKey(key);
        public void DeleteKey(string key) => Strings.Remove(key);
        public void Save() => SaveCalls++;
    }

    internal sealed class FakeDiscovery : IOidcDiscovery
    {
        public OidcConfiguration Configuration = new OidcConfiguration(
            "https://id-staging.xsolla.com",
            "https://id-staging.xsolla.com/oauth2/auth",
            "https://id-staging.xsolla.com/oauth2/token",
            "https://id-staging.xsolla.com/oauth2/userinfo",
            "https://id-staging.xsolla.com/oauth2/revoke");

        public int Calls;

        public Task<OidcConfiguration> GetAsync()
        {
            Calls++;
            return Task.FromResult(Configuration);
        }
    }

    internal sealed class FakeBrowser : IXsollaIdBrowser
    {
        public bool IsSupported { get; set; } = true;

        // Что вернуть. Если null — вернём код и присланный state (успех).
        public BrowserAuthResult? Result;

        // Подменить возвращаемый state, чтобы проверить защиту от CSRF.
        public string StateOverride;

        // Если выставлен — окно «висит», пока тест не завершит задачу. Нужно
        // для проверки single-flight: с мгновенным ответом второй вызов
        // случился бы уже после конца первого и ничего бы не проверил.
        public TaskCompletionSource<BrowserAuthResult> Gate;

        public int Calls;
        public string LastUrl;
        public string LastExpectedState;

        public Task<BrowserAuthResult> AuthorizeAsync(string authorizeUrl, string expectedState, TimeSpan timeout)
        {
            Calls++;
            LastUrl = authorizeUrl;
            LastExpectedState = expectedState;

            if (Gate != null)
                return Gate.Task;

            if (Result.HasValue)
                return Task.FromResult(Result.Value);

            return Task.FromResult(BrowserAuthResult.Ok("auth-code", StateOverride ?? expectedState));
        }
    }

    internal sealed class FakeTokenClient : IXsollaIdTokenClient
    {
        public TokenResponse ExchangeResult =
            TokenResponse.Ok(new AccessToken("access-1"), new RefreshToken("refresh-1"), 0);

        public TokenResponse RefreshResult =
            TokenResponse.Ok(new AccessToken("access-2"), new RefreshToken("refresh-2"), 0);

        public UserInfoResponse UserInfoResult =
            UserInfoResponse.Ok(new XsollaUserId("sub-1"), "Player One");

        public bool RevokeThrows;

        public int ExchangeCalls;
        public int RefreshCalls;
        public int UserInfoCalls;
        public int RevokeCalls;

        public string LastVerifier;

        public Task<TokenResponse> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri)
        {
            ExchangeCalls++;
            LastVerifier = codeVerifier;
            return Task.FromResult(ExchangeResult);
        }

        public Task<TokenResponse> RefreshAsync(string refreshToken)
        {
            RefreshCalls++;
            return Task.FromResult(RefreshResult);
        }

        public Task<UserInfoResponse> GetUserInfoAsync(string accessToken)
        {
            UserInfoCalls++;
            return Task.FromResult(UserInfoResult);
        }

        public Task RevokeAsync(string accessToken)
        {
            RevokeCalls++;

            if (RevokeThrows)
                throw new InvalidOperationException("405");

            return Task.CompletedTask;
        }
    }

    internal static class TestConfig
    {
        public static XsollaIdConfig Valid() => new XsollaIdConfig(
            XsollaIdEnvironment.Stage,
            // 127.0.0.1, а не localhost — так же, как зарегистрировано в
            // Xsolla ID и как теперь слушает serve.py.
            new XsollaIdClientRegistration(
                XsollaIdConfig.DefaultStageIssuer, "client-stage", "http://127.0.0.1:8081/xid-callback.html"),
            new XsollaIdClientRegistration(
                XsollaIdConfig.DefaultProductionIssuer, "client-prod", "https://example.com/xid-callback.html"),
            XsollaIdConfig.DefaultScopes,
            new[] { "http://127.0.0.1:8081" });
    }
}
