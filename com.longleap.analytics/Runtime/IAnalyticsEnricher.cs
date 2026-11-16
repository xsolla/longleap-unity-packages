namespace LongLeap.Analytics
{
    // Adds params to every event at Send time, after the service has stamped
    // session_id and engagement_time_msec. Register with
    // IAnalyticsService.AddEnricher; enrichers run in registration order.
    //
    // Use AnalyticsEnvelope.TrySetParam: it never overwrites a param the game
    // set and refuses once the event is full. An exception thrown here is
    // logged and the event is still sent.
    public interface IAnalyticsEnricher
    {
        void Enrich(AnalyticsEnvelope envelope);
    }
}
