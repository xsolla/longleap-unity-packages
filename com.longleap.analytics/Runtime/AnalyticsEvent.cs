namespace LongLeap.Analytics
{
    // A ready-made event for one-off cases where a dedicated class is overkill:
    //
    //     analytics.Send(new AnalyticsEvent("shop_opened").With("source", "main_menu"));
    //
    // Same validation as AnalyticsEventBase. For events sent from several
    // places, a typed subclass of AnalyticsEventBase keeps names and params in
    // one spot.
    public sealed class AnalyticsEvent : AnalyticsEventBase
    {
        public AnalyticsEvent(EventName name) : base(name)
        {
        }

        public AnalyticsEvent(string name) : base(new EventName(name))
        {
        }

        public AnalyticsEvent With(string key, string value)
        {
            SetParam(key, value);
            return this;
        }

        public AnalyticsEvent With(string key, long value)
        {
            SetParam(key, value);
            return this;
        }

        public AnalyticsEvent With(string key, double value)
        {
            SetParam(key, value);
            return this;
        }

        public AnalyticsEvent With(string key, bool value)
        {
            SetParam(key, value);
            return this;
        }
    }
}
