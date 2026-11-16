using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace LongLeap.XsollaId.Web.Tests
{
    // The HTTP transport for every provider test: canned replies in order, and a
    // log of every call, so tests can check how many requests went out and where.
    internal sealed class FakeTransport
    {
        private readonly Queue<HttpReply> _replies = new Queue<HttpReply>();

        public readonly List<HttpCall> Calls = new List<HttpCall>();

        // The reply once the queue is empty.
        public HttpReply WhenEmpty = HttpReply.Status(500);

        // When set, every request "hangs" until the test completes the task.
        // Needed to test sharing of in-flight loads.
        public TaskCompletionSource<HttpReply> Gate;

        public FakeTransport(params HttpReply[] replies)
        {
            foreach (HttpReply reply in replies)
                _replies.Enqueue(reply);
        }

        public List<string> Urls => Calls.ConvertAll(call => call.Url);

        public XsollaIdHttp AsHttp() => new XsollaIdHttp(call =>
        {
            Calls.Add(call);

            if (Gate != null)
                return Gate.Task;

            return Task.FromResult(_replies.Count > 0 ? _replies.Dequeue() : WhenEmpty);
        });
    }

    internal static class RequestAssert
    {
        // A JSON POST for one account, as leaderboards and leagues send a score.
        public static void AccountBody(
            HttpCall call, string gameId, string accountId, long score, string displayName)
        {
            Assert.AreEqual("POST", call.Method);
            Assert.AreEqual("application/json", call.ContentType);

            var body = JObject.Parse(call.Body);
            Assert.AreEqual(gameId, (string)body["gameId"]);
            Assert.AreEqual(accountId, (string)body["xsollaId"], "the account is sent, not a uid argument");
            Assert.AreEqual(score, (long)body["score"]);
            Assert.AreEqual(displayName, (string)body["displayName"]);
        }
    }
}
