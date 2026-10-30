using System;
using LongLeap.DailyRewards;
using UnityEngine;

namespace LongLeap.UI
{
    /// <summary>Optional host-owned presentation; the default card UI remains available.</summary>
    public interface IDailyRewardsPresentation
    {
        void Bind(IDailyRewardsService service, IUiSounds sounds, Func<string, Sprite> rewardIcon);
        void Refresh();
        void OnClosed();
    }
}
