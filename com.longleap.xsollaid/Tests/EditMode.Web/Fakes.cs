using System.Collections.Generic;
using System.Threading.Tasks;

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

        public FakeTransport(params HttpReply[] replies) => Enqueue(replies);

        public List<string> Urls => Calls.ConvertAll(call => call.Url);

        public FakeTransport Enqueue(params HttpReply[] replies)
        {
            foreach (HttpReply reply in replies)
                _replies.Enqueue(reply);

            return this;
        }

        public XsollaIdHttp AsHttp() => new XsollaIdHttp(call =>
        {
            Calls.Add(call);

            return Task.FromResult(_replies.Count > 0 ? _replies.Dequeue() : WhenEmpty);
        });
    }
}
