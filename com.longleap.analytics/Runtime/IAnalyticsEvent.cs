using System.Collections.Generic;

namespace LongLeap.Analytics
{
public interface IAnalyticsEvent
{
    EventName EventName { get; }
    IReadOnlyDictionary<string, object> Params { get; }
    long TimestampMicros { get; set; }
    long SessionId { get; set; }
    void SetParam(string key, object value);
}
}
