using NUnit.Framework;
using LongLeap.XsollaId.Leaderboards;
using LongLeap.Firebase;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.TestTools;

namespace LongLeap.Firebase.Tests
{
    [TestFixture]
    public class FirebaseLeaderboardProviderTests
    {
        private BoardId _boardId;

        [SetUp]
        public void Setup()
        {
            _boardId = new BoardId("kills");
            PlayerPrefs.DeleteKey("longleap_auth");
        }

        [TearDown]
        public void Teardown()
        {
            PlayerPrefs.DeleteKey("longleap_auth");
            PlayerPrefs.Save();
        }

        [Test]
        public async Task SubmitScore_NewEntry_ReturnsTrue()
        {
            var handler = new MockHttpHandler
            {
                AuthResponse = @"{
                    ""idToken"": ""test_token"",
                    ""refreshToken"": ""test_refresh"",
                    ""localId"": ""test_uid"",
                    ""expiresIn"": ""3600""
                }",
                ScoreResponses = new Queue<string>(new[] { "null" }),
                PatchResponses = new Queue<bool>(new[] { true })
            };

            var auth = new FirebaseAnonymousAuthProvider("test_key", handler.HandleAsync);
            var http = new FirebaseHttpClient("https://test.firebaseio.com", auth, handler.HandleAsync);
            var provider = new FirebaseLeaderboardProvider(http);
            var result = await provider.SubmitScoreAsync(_boardId, "uid1", 100, "Player_1", SortDirection.Descending);

            Assert.IsTrue(result);
        }

        [Test]
        public async Task GetTopAsync_NoAuth_ReturnsEmptyPage()
        {
            var handler = new MockHttpHandler
            {
                AuthResponse = @"{
                    ""idToken"": ""test_token"",
                    ""refreshToken"": ""test_refresh"",
                    ""localId"": ""test_uid"",
                    ""expiresIn"": ""3600""
                }",
                ScoreResponses = new Queue<string>(new[] { "null" }),
                QueryResponses = new Queue<string>(new[] { "{}" })
            };
            var auth = new FirebaseAnonymousAuthProvider("test_key", handler.HandleAsync);
            var http = new FirebaseHttpClient("https://test.firebaseio.com", auth, handler.HandleAsync);
            var provider = new FirebaseLeaderboardProvider(http);

            var page = await provider.GetTopAsync(_boardId, SortDirection.Descending, 10);
            Assert.AreEqual(0, page.Entries.Count);
        }

        private sealed class MockHttpHandler
        {
            public string AuthResponse { get; set; } = @"{
                ""idToken"": ""test_token"",
                ""refreshToken"": ""test_refresh"",
                ""localId"": ""test_uid"",
                ""expiresIn"": ""3600""
            }";
            public Queue<string> ScoreResponses { get; set; } = new Queue<string>();
            public Queue<string> QueryResponses { get; set; } = new Queue<string>();
            public Queue<bool> PatchResponses { get; set; } = new Queue<bool>();

            public async Task<string> HandleAsync(string method, string url, string body)
            {
                await Task.Yield();

                if (url.Contains("identitytoolkit") || url.Contains("securetoken"))
                    return AuthResponse;

                if (method == "PATCH")
                    return PatchResponses.Count > 0 && PatchResponses.Dequeue() ? "{}" : null;

                if (method == "GET")
                {
                    if (url.Contains("/score.json"))
                        return ScoreResponses.Count > 0 ? ScoreResponses.Dequeue() : "null";
                    return QueryResponses.Count > 0 ? QueryResponses.Dequeue() : "{}";
                }

                return "{}";
            }
        }
    }
}
