using System.Collections.Generic;

namespace LongLeap.Analytics
{
    // What the game hands to IAnalyticsService.Send: a name and its parameters.
    //
    // The service only reads an event: it copies it into an AnalyticsEnvelope
    // at Send time and never writes back. One instance may be sent many times
    // and changed between sends.
    //
    // Prefer subclassing AnalyticsEventBase (validates as you set params) or
    // using AnalyticsEvent for one-off events. Custom implementations are
    // validated when the envelope is built: invalid params are dropped with a
    // warning.
    public interface IAnalyticsEvent
    {
        EventName Name { get; }
        IReadOnlyDictionary<string, object> Params { get; }
    }
}
