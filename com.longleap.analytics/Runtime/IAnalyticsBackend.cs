using System;
using System.Collections.Generic;
using UnityEngine;

namespace LongLeap.Analytics
{
public interface IAnalyticsBackend
{
    void SendBatch(IReadOnlyList<IAnalyticsEvent> events);
}
}
