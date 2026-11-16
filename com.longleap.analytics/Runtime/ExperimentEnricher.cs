using System;
using System.Collections.Generic;
using System.Text;
using LongLeap.Core;
using UnityEngine;

namespace LongLeap.Analytics
{
    // Tags every event with the player's active A/B experiments:
    // exp_{experimentId} = variantIndex. The provider is usually
    // RemoteConfigService (com.longleap.remoteconfig).
    //
    //     analytics.AddEnricher(new ExperimentEnricher(remoteConfig));
    //
    // The experiment ID is turned into a valid parameter name: characters
    // other than letters, digits and '_' become '_', and the name is cut to
    // 40 characters. Experiment tags use the slots reserved for the service;
    // tags that do not fit are skipped with a single warning per experiment.
    public sealed class ExperimentEnricher : IAnalyticsEnricher
    {
        public const string ParamPrefix = "exp_";

        private readonly IExperimentInfoProvider _provider;
        private readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);

        public ExperimentEnricher(IExperimentInfoProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public void Enrich(AnalyticsEnvelope envelope)
        {
            var experiments = _provider.ActiveExperiments;
            if (envelope == null || experiments == null || experiments.Count == 0)
                return;

            foreach (var experiment in experiments)
            {
                var key = ToParamName(experiment.ExperimentId);
                if (key == null)
                {
                    WarnOnce(experiment.ExperimentId ?? "<null>",
                        $"ExperimentEnricher: experiment ID '{experiment.ExperimentId}' cannot be turned into a parameter name; it is not tagged.");
                    continue;
                }

                if (!envelope.TrySetParam(key, (long)experiment.VariantIndex))
                {
                    WarnOnce(key,
                        $"ExperimentEnricher: no free parameter slot for '{key}' on event '{envelope.Name}' (or the event already sets it); the tag is skipped.");
                }
            }
        }

        // "exp_" + the sanitized experiment ID, or null for an empty ID.
        public static string ToParamName(string experimentId)
        {
            if (string.IsNullOrEmpty(experimentId))
                return null;

            var builder = new StringBuilder(ParamPrefix.Length + experimentId.Length);
            builder.Append(ParamPrefix);
            foreach (var c in experimentId)
            {
                var isSafe = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';
                builder.Append(isSafe ? c : '_');
            }

            if (builder.Length > Ga4Rules.MaxNameLength)
                builder.Length = Ga4Rules.MaxNameLength;

            return builder.ToString();
        }

        private void WarnOnce(string key, string message)
        {
            if (_warned.Add(key))
                Debug.LogWarning(message);
        }
    }
}
