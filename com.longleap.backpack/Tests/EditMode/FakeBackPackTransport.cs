using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LongLeap.Backpack.Tests
{
    // Records every call and answers from a queue; an empty queue answers 500.
    // Hang: the call never completes until its token is cancelled — for
    // cancellation and Dispose tests. Throw: SendAsync throws instead.
    internal sealed class FakeBackPackTransport : IBackPackTransport
    {
        private readonly Queue<BackPackHttpReply> _replies = new Queue<BackPackHttpReply>();

        public readonly List<BackPackHttpCall> Calls = new List<BackPackHttpCall>();

        public bool Hang;
        public Exception Throw;

        public FakeBackPackTransport(params BackPackHttpReply[] replies)
        {
            foreach (BackPackHttpReply reply in replies)
                _replies.Enqueue(reply);
        }

        public BackPackHttpCall LastCall => Calls[Calls.Count - 1];

        public Task<BackPackHttpReply> SendAsync(BackPackHttpCall call, CancellationToken cancellationToken)
        {
            Calls.Add(call);

            if (Throw != null)
                throw Throw;

            if (Hang)
            {
                var pending = new TaskCompletionSource<BackPackHttpReply>();
                cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));
                return pending.Task;
            }

            return Task.FromResult(_replies.Count > 0 ? _replies.Dequeue() : BackPackHttpReply.Status(500));
        }
    }

    internal static class BackPackTestData
    {
        public const string BaseUrl = "http://127.0.0.1:5066";
        public const string GameId = "game_01";
        public const string XsollaId = "player-1";

        public static BackPackEndpoint Endpoint() => new BackPackEndpoint(BaseUrl + "/", GameId);

        public static BackPackService Service(FakeBackPackTransport transport, string xsollaId = XsollaId) =>
            new BackPackService(() => xsollaId, Endpoint(), transport);
    }
}
