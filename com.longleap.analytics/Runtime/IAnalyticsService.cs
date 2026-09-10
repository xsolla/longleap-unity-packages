using System;

namespace LongLeap.Analytics
{
public interface IAnalyticsService : IDisposable
{
    float FlushIntervalSec { get; }
    void Send(IAnalyticsEvent e);
    void Flush();
    bool IsDisposed { get; }
}
}
