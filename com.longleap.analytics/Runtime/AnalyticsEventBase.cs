using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace LongLeap.Analytics
{
public abstract class AnalyticsEventBase : IAnalyticsEvent
{
    private static readonly Regex KeyPattern = new Regex(@"^[a-zA-Z_][a-zA-Z0-9_]{0,39}$", RegexOptions.Compiled);
    private const int MaxParams = 25;
    private const int MaxStringLength = 100;

    private readonly Dictionary<string, object> _params = new Dictionary<string, object>();

    public EventName EventName { get; }
    public IReadOnlyDictionary<string, object> Params => _params;
    public long TimestampMicros { get; set; } = 0;
    public long SessionId { get; set; } = 0;

    protected AnalyticsEventBase(EventName eventName)
    {
        EventName = eventName;
    }

    public void SetParam(string key, object value)
    {
        if (string.IsNullOrEmpty(key) || !KeyPattern.IsMatch(key))
        {
            Debug.LogWarning($"AnalyticsEventBase: недопустимый ключ параметра '{key}'.");
            return;
        }

        if (_params.Count >= MaxParams)
        {
            Debug.LogWarning($"AnalyticsEventBase: превышен лимит параметров ({MaxParams}). Параметр '{key}' пропущен.");
            return;
        }

        if (value is string str && str.Length > MaxStringLength)
        {
            Debug.LogWarning($"AnalyticsEventBase: значение параметра '{key}' превышает {MaxStringLength} символов. Будет обрезано.");
            value = str.Substring(0, MaxStringLength);
        }

        _params[key] = value;
    }
}
}
