using System;
using System.Collections.Generic;
using LongLeap.Core;

namespace LongLeap.RemoteConfig
{
    /// <summary>
    /// What an <see cref="IRemoteConfigProvider"/> returns from one fetch. Collections are never null.
    /// </summary>
    public sealed class RemoteConfigFetchResult
    {
        private static readonly IReadOnlyDictionary<string, string> NoParameters = new Dictionary<string, string>();

        /// <summary>The fetch did not reach the server or the response was unusable.</summary>
        public static RemoteConfigFetchResult Failure { get; } =
            new RemoteConfigFetchResult(false, NoParameters, Array.Empty<AbExperimentInfo>());

        /// <summary>
        /// The server answered. <paramref name="parameters"/> is the complete parameter set and
        /// replaces the previous one; <c>null</c> means "no parameters are published".
        /// </summary>
        /// <param name="parameters">Raw parameter values by key.</param>
        /// <param name="experiments">Experiments the player is in; <c>null</c> means none.</param>
        public static RemoteConfigFetchResult Success(
            IReadOnlyDictionary<string, string> parameters,
            IReadOnlyList<AbExperimentInfo> experiments = null)
        {
            return new RemoteConfigFetchResult(
                true,
                parameters ?? NoParameters,
                experiments ?? Array.Empty<AbExperimentInfo>());
        }

        /// <summary><c>true</c> if the server answered and <see cref="Parameters"/> is the full set.</summary>
        public bool IsSuccess { get; }

        /// <summary>Raw parameter values by key. Empty on failure.</summary>
        public IReadOnlyDictionary<string, string> Parameters { get; }

        /// <summary>A/B experiments the player is assigned to. Empty on failure.</summary>
        public IReadOnlyList<AbExperimentInfo> Experiments { get; }

        private RemoteConfigFetchResult(
            bool isSuccess,
            IReadOnlyDictionary<string, string> parameters,
            IReadOnlyList<AbExperimentInfo> experiments)
        {
            IsSuccess = isSuccess;
            Parameters = parameters;
            Experiments = experiments;
        }
    }
}
