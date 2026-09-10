using System.Collections.Generic;
using LongLeap.Core;

namespace LongLeap.RemoteConfig
{
    public sealed class RemoteConfigFetchResult
    {
        public IReadOnlyDictionary<string, string> Parameters { get; }
        public IReadOnlyList<AbExperimentInfo> Experiments { get; }
        public bool IsSuccess { get; }

        public RemoteConfigFetchResult(
            IReadOnlyDictionary<string, string> parameters,
            IReadOnlyList<AbExperimentInfo> experiments,
            bool isSuccess)
        {
            Parameters = parameters;
            Experiments = experiments;
            IsSuccess = isSuccess;
        }
    }
}
