using System;
using System.Collections.Generic;
using LongLeap.Core;
using UnityEngine;

namespace LongLeap.Analytics
{
public sealed class AnalyticsService : IAnalyticsService
{
    private readonly int _batchSize;
    private readonly float _flushIntervalSec;
    private readonly IAnalyticsBackend _backend;
    private readonly Queue<IAnalyticsEvent> _queue = new Queue<IAnalyticsEvent>();
    private readonly long _sessionId;
    private long _lastEventTimeMicros;
    private bool _isFlushing;
    private bool _disposed;
    private IExperimentInfoProvider _experimentProvider;

    public AnalyticsService(int batchSize = 10, float flushIntervalSec = 5f, IAnalyticsBackend backend = null)
    {
        _batchSize = batchSize;
        _flushIntervalSec = flushIntervalSec;
        _backend = backend;
        _sessionId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public float FlushIntervalSec => _flushIntervalSec;
    public bool IsDisposed => _disposed;

    public void SetExperimentInfoProvider(IExperimentInfoProvider provider)
    {
        _experimentProvider = provider;
    }

    public void Send(IAnalyticsEvent e)
    {
        if (e == null || _disposed)
            return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000;
        e.TimestampMicros = now;
        e.SessionId = _sessionId;

        if (!e.Params.ContainsKey("engagement_time_msec"))
        {
            var engagementMsec = _lastEventTimeMicros > 0
                ? (now - _lastEventTimeMicros) / 1000
                : 0L;
            e.SetParam("engagement_time_msec", engagementMsec);
        }
        _lastEventTimeMicros = now;

        if (_experimentProvider != null)
        {
            var experiments = _experimentProvider.ActiveExperiments;
            if (experiments != null && experiments.Count > 0)
            {
                foreach (var exp in experiments)
                    e.SetParam($"exp_{exp.ExperimentId}", exp.VariantIndex);
            }
        }

        _queue.Enqueue(e);

        if (_queue.Count >= _batchSize)
            Flush();
    }

    public void Flush()
    {
        if (_isFlushing || _queue.Count == 0)
            return;

        _isFlushing = true;

        try
        {
            var batch = new List<IAnalyticsEvent>(_queue.Count);
            while (_queue.Count > 0)
                batch.Add(_queue.Dequeue());

            try
            {
                _backend?.SendBatch(batch);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"AnalyticsService: ошибка при отправке батча: {ex.Message}");
                foreach (var evt in batch)
                    _queue.Enqueue(evt);
            }
        }
        finally
        {
            _isFlushing = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Flush();
        while (_queue.Count > 0)
            _queue.Dequeue();
    }
}
}
