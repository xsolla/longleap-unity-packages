using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace LongLeap.Analytics
{
    // Queues events and hands them to an IAnalyticsBackend in batches.
    //
    // Send pipeline:
    //   event -> AnalyticsEnvelope (copy, validated) -> session_id and
    //   engagement_time_msec -> enrichers -> bounded queue
    //
    // Flush triggers: the queue reaching BatchSize, Update every
    // FlushIntervalSeconds, SetForeground(false), Flush/FlushAsync, Dispose.
    // One delivery is in flight at a time. A flush requested meanwhile runs
    // right after it if that delivery succeeded; after a failure the backoff
    // decides when the next attempt happens. To keep a backend that sends
    // events of its own (a log forwarder, say) from looping forever, one run
    // makes at most MaxRoundsPerRun deliveries; anything left goes out on a
    // later trigger.
    //
    // Failures: events the backend reports as RetryLater go back to the front
    // of the queue (order is kept) and automatic flushes pause with
    // exponential backoff. Only failures the server answered count against
    // MaxAttempts; network errors do not, so an offline session waits until
    // it can be sent. The queue is capped at MaxQueueSize (oldest dropped) and
    // events older than MaxEventAge are dropped before sending. The queue
    // lives in memory only: whatever is queued when the process dies is lost.
    //
    // Sessions follow GA4 semantics: a new session starts after SessionTimeout
    // of inactivity (no events, or time spent in background). The session ID
    // is the session start in Unix seconds.
    //
    // engagement_time_msec is the foreground time since the previous event,
    // at least 1 ms: GA4 needs a non-zero value to count the user as active.
    // Time after the last event is only reported with the next one; set
    // AnalyticsOptions.BackgroundEvent to report it when the app goes to
    // background.
    //
    // Main thread only.
    public sealed class AnalyticsService : IAnalyticsService
    {
        private const long MicrosPerSecond = 1_000_000L;
        private const int MaxRoundsPerRun = 4;

        private readonly IAnalyticsBackend _backend;
        private readonly Func<DateTimeOffset> _clock;
        private readonly int _batchSize;
        private readonly float _flushIntervalSeconds;
        private readonly int _maxQueueSize;
        private readonly int _maxAttempts;
        private readonly float _maxRetryDelaySeconds;
        private readonly long _sessionTimeoutMicros;
        private readonly long _maxEventAgeMicros;
        private readonly EventName _backgroundEvent;

        private readonly List<AnalyticsEnvelope> _queue = new List<AnalyticsEnvelope>();
        private readonly List<IAnalyticsEnricher> _enrichers = new List<IAnalyticsEnricher>();
        private readonly Dictionary<string, string> _userProperties = new Dictionary<string, string>(StringComparer.Ordinal);

        // Handed to envelopes; replaced (never mutated) when properties change.
        private IReadOnlyDictionary<string, string> _userPropertiesSnapshot;
        private string _userId;
        private bool _enabled;
        private bool _disposed;

        // Bumped when consent is withdrawn: a batch taken before that must not
        // come back into the queue if consent is given again meanwhile.
        private int _consentGeneration;

        private long _sessionId;
        private long _lastActivityMicros;
        private long _engagedMicros;
        private long _foregroundSinceMicros;
        private bool _isForeground = true;

        private bool _isFlushing;
        private bool _flushAgain;
        private TaskCompletionSource<bool> _runCompletion;
        private float _secondsSinceFlush;
        private float _retryDelayRemaining;
        private int _consecutiveFailures;
        private bool _overflowWarned;

        public AnalyticsService(IAnalyticsBackend backend, AnalyticsOptions options = null)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));

            options ??= new AnalyticsOptions();
            options.Validate();

            _clock = options.Clock;
            _batchSize = options.BatchSize;
            _flushIntervalSeconds = options.FlushIntervalSeconds;
            _maxQueueSize = options.MaxQueueSize;
            _maxAttempts = options.MaxAttempts;
            _maxRetryDelaySeconds = options.MaxRetryDelaySeconds;
            _sessionTimeoutMicros = ToMicros(options.SessionTimeout);
            _maxEventAgeMicros = ToMicros(options.MaxEventAge);
            _backgroundEvent = options.BackgroundEvent;
            _enabled = options.CollectionEnabled;

            var now = NowMicros();
            _foregroundSinceMicros = now;
            StartSession(now);
        }

        public bool IsCollectionEnabled => _enabled;

        public long SessionId => _sessionId;

        public int PendingCount => _queue.Count;

        public bool IsDisposed => _disposed;

        public void SetCollectionEnabled(bool enabled)
        {
            if (_enabled == enabled)
                return;

            _enabled = enabled;
            if (!enabled)
            {
                _consentGeneration++;
                _queue.Clear();
                _overflowWarned = false;
                return;
            }

            // Time spent without consent is not engagement to report.
            _engagedMicros = 0L;
            if (_isForeground)
                _foregroundSinceMicros = NowMicros();
        }

        public void SetUserId(string userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                _userId = null;
                return;
            }

            if (userId.Length > Ga4Rules.MaxUserIdLength)
            {
                Debug.LogWarning($"AnalyticsService: user ID ignored: longer than {Ga4Rules.MaxUserIdLength} characters.");
                return;
            }

            _userId = userId;
        }

        public void SetUserProperty(string name, string value)
        {
            if (!Ga4Rules.IsValidUserPropertyName(name, out var reason))
            {
                Debug.LogWarning($"AnalyticsService: user property '{name}' ignored: {reason}.");
                return;
            }

            if (value == null)
            {
                if (_userProperties.Remove(name))
                    RefreshUserPropertiesSnapshot();
                return;
            }

            if (!_userProperties.ContainsKey(name) && _userProperties.Count >= Ga4Rules.MaxUserProperties)
            {
                Debug.LogWarning($"AnalyticsService: user property '{name}' ignored: at most {Ga4Rules.MaxUserProperties} user properties.");
                return;
            }

            if (value.Length > Ga4Rules.MaxUserPropertyValueLength)
            {
                Debug.LogWarning($"AnalyticsService: user property '{name}' cut to {Ga4Rules.MaxUserPropertyValueLength} characters.");
                value = Ga4Rules.Truncate(value, Ga4Rules.MaxUserPropertyValueLength);
            }

            if (_userProperties.TryGetValue(name, out var current) && current == value)
                return;

            _userProperties[name] = value;
            RefreshUserPropertiesSnapshot();
        }

        public void AddEnricher(IAnalyticsEnricher enricher)
        {
            if (enricher == null)
                throw new ArgumentNullException(nameof(enricher));

            if (!_enrichers.Contains(enricher))
                _enrichers.Add(enricher);
        }

        public void RemoveEnricher(IAnalyticsEnricher enricher)
        {
            _enrichers.Remove(enricher);
        }

        public void Send(IAnalyticsEvent analyticsEvent)
        {
            if (_disposed || !_enabled)
                return;

            if (analyticsEvent == null)
            {
                Debug.LogWarning("AnalyticsService: Send called with null; ignored.");
                return;
            }

            if (!analyticsEvent.Name.IsValid)
            {
                Debug.LogWarning($"AnalyticsService: {analyticsEvent.GetType().Name} dropped: it has no valid event name.");
                return;
            }

            var now = NowMicros();
            if (now - _lastActivityMicros > _sessionTimeoutMicros)
                StartSession(now);
            _lastActivityMicros = now;

            var envelope = new AnalyticsEnvelope(
                analyticsEvent.Name, analyticsEvent.Params, now, _sessionId, _userId, _userPropertiesSnapshot);

            envelope.SetServiceParam(Ga4Rules.SessionIdParam, _sessionId);
            envelope.SetServiceParam(Ga4Rules.EngagementTimeParam, Math.Max(1L, TakeEngagedMicros(now) / 1000L));

            for (var i = 0; i < _enrichers.Count; i++)
            {
                try
                {
                    _enrichers[i].Enrich(envelope);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }

            Enqueue(envelope);

            if (_queue.Count >= _batchSize && _retryDelayRemaining <= 0f)
                Flush();
        }

        public void Update(float unscaledDeltaTime)
        {
            if (_disposed || unscaledDeltaTime <= 0f)
                return;

            if (_retryDelayRemaining > 0f)
            {
                _retryDelayRemaining -= unscaledDeltaTime;
                if (_retryDelayRemaining > 0f)
                    return;

                _retryDelayRemaining = 0f;
                Flush();
                return;
            }

            _secondsSinceFlush += unscaledDeltaTime;
            if (_secondsSinceFlush >= _flushIntervalSeconds && _queue.Count > 0)
                Flush();
        }

        public void SetForeground(bool isForeground)
        {
            if (_disposed || _isForeground == isForeground)
                return;

            var now = NowMicros();
            _isForeground = isForeground;

            if (isForeground)
            {
                if (now - _lastActivityMicros > _sessionTimeoutMicros)
                    StartSession(now);

                _foregroundSinceMicros = now;
                _lastActivityMicros = now;
                return;
            }

            // Leaving: bank the engaged time, optionally report it, and push
            // out what is queued while the app still runs (mobile may suspend
            // it and a browser may close the tab right after this).
            _engagedMicros += Math.Max(0L, now - _foregroundSinceMicros);
            _lastActivityMicros = now;

            if (_backgroundEvent.IsValid && _enabled && _engagedMicros >= 1000L)
                Send(new AnalyticsEvent(_backgroundEvent));

            Flush();
        }

        public void Flush()
        {
            _ = FlushAsync();
        }

        public Task FlushAsync()
        {
            if (_isFlushing)
            {
                _flushAgain = true;
                return _runCompletion.Task;
            }

            if (_queue.Count == 0)
                return Task.CompletedTask;

            var completion = new TaskCompletionSource<bool>();
            _runCompletion = completion;
            _ = RunFlushAsync(completion);
            return completion.Task;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            // Last delivery attempt. Whatever fails here is not retried.
            _disposed = true;
            Flush();
            _enrichers.Clear();
        }

        private async Task RunFlushAsync(TaskCompletionSource<bool> completion)
        {
            _isFlushing = true;
            try
            {
                for (var round = 0; round < MaxRoundsPerRun; round++)
                {
                    _flushAgain = false;
                    _secondsSinceFlush = 0f;

                    var batch = TakeBatch();
                    if (batch == null)
                        break;

                    var generation = _consentGeneration;
                    SendResult result;
                    try
                    {
                        result = await _backend.SendAsync(batch);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"AnalyticsService: backend {_backend.GetType().Name} threw: {ex}");
                        result = SendResult.RetryLater(0, ex.Message, batch.Length);
                    }

                    if (!result.IsComplete)
                    {
                        Requeue(batch, result, generation);
                        break;
                    }

                    _consecutiveFailures = 0;
                    _retryDelayRemaining = 0f;

                    if (!_flushAgain)
                        break;
                }
            }
            finally
            {
                _isFlushing = false;
                _runCompletion = null;
                completion.TrySetResult(true);
            }
        }

        // Removes and returns the whole queue minus events GA4 would discard
        // as too old; null when nothing is left to send.
        private AnalyticsEnvelope[] TakeBatch()
        {
            if (_queue.Count == 0)
                return null;

            var oldestAllowed = NowMicros() - _maxEventAgeMicros;
            var expired = _queue.RemoveAll(e => e.TimestampMicros < oldestAllowed);
            if (expired > 0)
                Debug.LogWarning($"AnalyticsService: {expired} event(s) dropped: older than {_maxEventAgeMicros / MicrosPerSecond / 3600} h.");

            if (_queue.Count == 0)
                return null;

            var batch = _queue.ToArray();
            _queue.Clear();
            _overflowWarned = false;
            return batch;
        }

        private void Requeue(AnalyticsEnvelope[] batch, SendResult result, int generation)
        {
            if (_disposed || !_enabled || generation != _consentGeneration)
                return;

            var handled = Math.Min(result.HandledCount, batch.Length);
            var chargedEnd = Math.Min(batch.Length, handled + result.AttemptedCount);

            var retry = new List<AnalyticsEnvelope>(batch.Length - handled);
            var dropped = 0;
            for (var i = handled; i < batch.Length; i++)
            {
                var envelope = batch[i];
                if (i < chargedEnd)
                    envelope.Attempts++;

                if (envelope.Attempts >= _maxAttempts)
                    dropped++;
                else
                    retry.Add(envelope);
            }

            // Retried events are older than anything sent meanwhile: they go first.
            _queue.InsertRange(0, retry);
            while (_queue.Count > _maxQueueSize)
            {
                _queue.RemoveAt(0);
                dropped++;
            }

            _consecutiveFailures++;
            var exponent = Math.Min(_consecutiveFailures - 1, 16);
            _retryDelayRemaining = Math.Min(_maxRetryDelaySeconds, _flushIntervalSeconds * (1 << exponent));

            var droppedNote = dropped > 0 ? $"; {dropped} event(s) dropped after {_maxAttempts} failed attempts or queue overflow" : string.Empty;
            Debug.LogWarning(
                $"AnalyticsService: delivery failed ({result.Error}). {_queue.Count} event(s) queued, " +
                $"next attempt in {_retryDelayRemaining:0.#} s{droppedNote}.");
        }

        private void Enqueue(AnalyticsEnvelope envelope)
        {
            if (_queue.Count >= _maxQueueSize)
            {
                _queue.RemoveAt(0);
                if (!_overflowWarned)
                {
                    _overflowWarned = true;
                    Debug.LogWarning($"AnalyticsService: queue is full ({_maxQueueSize}); dropping the oldest events until it drains.");
                }
            }

            _queue.Add(envelope);
        }

        private void StartSession(long nowMicros)
        {
            var id = nowMicros / MicrosPerSecond;
            _sessionId = id > _sessionId ? id : _sessionId + 1;
            _lastActivityMicros = nowMicros;
            _engagedMicros = 0L;
            if (_isForeground)
                _foregroundSinceMicros = nowMicros;
        }

        private long TakeEngagedMicros(long nowMicros)
        {
            var total = _engagedMicros;
            if (_isForeground)
            {
                total += Math.Max(0L, nowMicros - _foregroundSinceMicros);
                _foregroundSinceMicros = nowMicros;
            }

            _engagedMicros = 0L;
            return total;
        }

        private void RefreshUserPropertiesSnapshot()
        {
            _userPropertiesSnapshot = _userProperties.Count == 0
                ? null
                : new Dictionary<string, string>(_userProperties, StringComparer.Ordinal);
        }

        private long NowMicros() => _clock().ToUnixTimeMilliseconds() * 1000L;

        private static long ToMicros(TimeSpan span) => (long)(span.TotalMilliseconds * 1000.0);
    }
}
