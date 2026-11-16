using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LongLeap.Core;

namespace LongLeap.RemoteConfig.Tests
{
    // Returns Result synchronously, or waits on Pending when it is set.
    internal sealed class FakeRemoteConfigProvider : IRemoteConfigProvider
    {
        public RemoteConfigFetchResult Result { get; set; } = RemoteConfigFetchResult.Failure;
        public TaskCompletionSource<RemoteConfigFetchResult> Pending { get; set; }
        public Exception Throw { get; set; }
        public int CallCount { get; private set; }
        public CancellationToken LastToken { get; private set; }

        public Task<RemoteConfigFetchResult> FetchAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastToken = cancellationToken;
            if (Throw != null)
                throw Throw;
            return Pending != null ? Pending.Task : Task.FromResult(Result);
        }

        public static RemoteConfigFetchResult Success(params (string key, string value)[] parameters) =>
            Success(parameters, null);

        public static RemoteConfigFetchResult Success((string key, string value)[] parameters, IReadOnlyList<AbExperimentInfo> experiments)
        {
            var dict = new Dictionary<string, string>();
            foreach (var (key, value) in parameters)
                dict[key] = value;
            return RemoteConfigFetchResult.Success(dict, experiments);
        }
    }
}
