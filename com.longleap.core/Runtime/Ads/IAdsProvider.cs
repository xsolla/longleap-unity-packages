using System;

namespace LongLeap.Core.Ads
{
    // Portal ads. Game code works only with this interface and knows nothing
    // about the specific portal: com.longleap.gamepix implements it for GamePix,
    // NullAdsProvider (com.longleap.nullproviders) for builds without a portal.
    public interface IAdsProvider
    {
        // Whether there is an ad portal at all. Use it to hide "watch an ad"
        // buttons: outside a portal they could never work.
        // Differs from IsRewardedReady, which means "can be requested right now".
        bool IsAvailable { get; }

        // Whether a rewarded ad can be requested right now. False while the
        // previous request is still pending: portals usually ignore a repeated
        // call silently, so disable the button instead of letting it be pressed
        // for nothing.
        bool IsRewardedReady { get; }

        // Whether an ad is on screen right now. The portal pauses time and audio
        // itself, but code on unscaled time (WaitForSecondsRealtime,
        // unscaledDeltaTime, async continuations) keeps running and should be
        // gated by this flag.
        bool IsAdShowing { get; }

        // Rewarded ad. onComplete is called exactly once.
        void ShowRewarded(Action<AdResult> onComplete);

        // Interstitial ad. onComplete is called exactly once.
        void ShowInterstitial(Action<AdResult> onComplete);

        // Current run score for the portal's showcase. No-op outside a portal.
        void ReportScore(long score);

        // Call every frame (e.g. from the bootstrapper). Portals do not send a
        // "show did not happen" event, so the provider closes stale requests
        // itself on a timer. Empty in the null provider.
        void Tick();
    }
}
