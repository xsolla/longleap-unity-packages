using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace LongLeap.Analytics
{
    // Logs every event to the Unity console instead of sending it. For the
    // Editor and debug builds, or as a stand-in until GA4 is configured.
    public sealed class ConsoleAnalyticsBackend : IAnalyticsBackend
    {
        private static readonly Task<SendResult> Completed = Task.FromResult(SendResult.Complete);

        public Task<SendResult> SendAsync(IReadOnlyList<AnalyticsEnvelope> batch)
        {
            if (batch == null)
                return Completed;

            var builder = new StringBuilder();
            foreach (var e in batch)
            {
                builder.Clear();
                builder.Append("Analytics: ").Append(e.Name.Value).Append(" {");

                var first = true;
                foreach (var pair in e.Params)
                {
                    builder.Append(first ? " " : ", ").Append(pair.Key).Append('=');
                    builder.Append(System.Convert.ToString(pair.Value, CultureInfo.InvariantCulture));
                    first = false;
                }

                builder.Append(" }");
                if (e.UserId != null)
                    builder.Append(" user=").Append(e.UserId);

                Debug.Log(builder.ToString());
            }

            return Completed;
        }
    }
}
