namespace LongLeap.XsollaId
{
    // Persists the session between launches.
    //
    // A separate seam rather than direct PlayerPrefs access, so tests run the
    // state machine on InMemoryPlayerPrefs and do not depend on execution order
    // (PlayerPrefs keys are global and outlive a test).
    public interface IXsollaIdSessionStore
    {
        // null means there is no saved session, or it is unreadable.
        XsollaIdSession Load();

        void Save(XsollaIdSession session);

        void Clear();
    }
}
