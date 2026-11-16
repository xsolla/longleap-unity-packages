using System;
using LongLeap.Core.Ads;

namespace LongLeap.Null
{
    /// <summary>
    /// Ads backend for builds without an ad network (anything but a portal
    /// build): there are no ads and never will be.
    /// </summary>
    // Returning this instead of null is deliberate: calling code needs no null
    // checks and gets an honest "no" to every question.
    public sealed class NullAdsProvider : IAdsProvider
    {
        public bool IsAvailable => false;

        public bool IsRewardedReady => false;

        public bool IsAdShowing => false;

        public void ShowRewarded(Action<AdResult> onComplete)
        {
            // Logged on purpose: the overlay then shows at once that this is a
            // build without ads, not that ads are "broken".
            AdsDebug.Record("REWARDED: no ads provider -> Unavailable");

            onComplete?.Invoke(AdResult.Unavailable);
        }

        public void ShowInterstitial(Action<AdResult> onComplete)
        {
            AdsDebug.Record("INTERSTITIAL: no ads provider -> Unavailable");

            onComplete?.Invoke(AdResult.Unavailable);
        }

        public void ReportScore(long score)
        {
        }

        public void Tick()
        {
        }
    }
}
