using System;
using GamePix;
using LongLeap.Core.Ads;
using UnityEngine;

namespace LongLeap.GamePix
{
    // The only place that touches Gpx.Ads and Gpx.Events. The whole assembly is
    // gated by the "GAMEPIX" defineConstraint, so no #if is needed inside.
    //
    // The vendor IAds is not usable by a game as is; this class adds three things:
    //   • a single request at a time — the plugin silently ignores a second call
    //     until the first completes ("[GPX] Reward ad already called"); without
    //     this flag the UI would offer a button that does nothing and says nothing;
    //   • a timeout — the callback may never arrive (ad blocker, offline);
    //   • a one-shot result — the callback is cleared before it is invoked, so a
    //     late answer from the plugin cannot grant the reward a second time.
    //
    // The host game MUST call Tick() every frame (see Tick): the plugin has no
    // "the ad never showed" event, so stale requests are closed from there.
    public sealed class GamePixAdsProvider : IAdsProvider
    {
        // How long we wait for the portal to RESPOND to a request. This is not
        // the length of the ad: while the ad is playing the deadline is pushed
        // back (see Tick).
        private const float RequestTimeoutSeconds = 30f;

        // Absolute upper bound on a request's lifetime. Covers the case where
        // the portal paused the game and never unpaused it — e.g. the tab was
        // minimised mid-ad and the callback will never come. Without it the
        // request would hang forever and the button would stay disabled for
        // the rest of the run.
        private const float MaxRequestLifetimeSeconds = 600f;

        private readonly global::GamePix.Ads.IAds _ads;
        private readonly global::GamePix.Events.IEvents _events;
        private readonly Func<float> _now;
        private readonly Func<bool> _isAdShowing;

        private Action<AdResult> _rewardedCallback;
        private Action<AdResult> _interstitialCallback;

        private float _rewardedDeadline;
        private float _interstitialDeadline;

        // The moment after which the request is closed no matter what the
        // portal says.
        private float _rewardedExpiry;
        private float _interstitialExpiry;

        // Production constructor: uses whatever the plugin provides (in the
        // Editor these are its own UnityAds/UnityEvents stubs).
        public GamePixAdsProvider()
            : this(Gpx.Ads, Gpx.Events, () => Time.realtimeSinceStartup, () => Gpx.IsGamePaused)
        {
        }

        // For tests: the vendor Editor stubs always answer with success, so
        // neither a refusal nor a timeout can be tested through them — a
        // substitute is needed. The "ad on screen" flag is substitutable too:
        // in the Editor Gpx.IsGamePaused is always false, so behaviour during a
        // show could not be tested otherwise.
        public GamePixAdsProvider(global::GamePix.Ads.IAds ads, global::GamePix.Events.IEvents events, Func<float> now)
            : this(ads, events, now, () => Gpx.IsGamePaused)
        {
        }

        public GamePixAdsProvider(global::GamePix.Ads.IAds ads, global::GamePix.Events.IEvents events,
            Func<float> now, Func<bool> isAdShowing)
        {
            _ads = ads;
            _events = events;
            _now = now;
            _isAdShowing = isAdShowing;
        }

        // The provider only exists in a portal build, so the portal is there.
        public bool IsAvailable => true;

        public bool IsRewardedReady => _rewardedCallback == null;

        public bool IsAdShowing => _isAdShowing();

        public void ShowRewarded(Action<AdResult> onComplete)
        {
            if (_rewardedCallback != null)
            {
                // A request is already in flight. The plugin would ignore this
                // call anyway — answer ourselves so the caller does not wait
                // for a callback that will never come.
                AdsDebug.Record("REWARDED: rejected - previous request still in flight");
                onComplete?.Invoke(AdResult.Failed);
                return;
            }

            _rewardedCallback = onComplete;
            _rewardedDeadline = _now() + RequestTimeoutSeconds;
            _rewardedExpiry = _now() + MaxRequestLifetimeSeconds;

            AdsDebug.Record("REWARDED: request sent to portal");

            _ads.RewardAd(OnRewardedShown, OnRewardedFailed);
        }

        public void ShowInterstitial(Action<AdResult> onComplete)
        {
            if (_interstitialCallback != null)
            {
                onComplete?.Invoke(AdResult.Failed);
                return;
            }

            _interstitialCallback = onComplete;
            _interstitialDeadline = _now() + RequestTimeoutSeconds;
            _interstitialExpiry = _now() + MaxRequestLifetimeSeconds;

            AdsDebug.Record("INTERSTITIAL: request sent to portal");

            _ads.InterstitialAd(OnInterstitialShown, OnInterstitialFailed);
        }

        public void ReportScore(long score)
        {
            // The portal's score is an int; a run never gets that high, but it
            // must not be truncated silently.
            int clamped = score > int.MaxValue ? int.MaxValue : score < 0 ? 0 : (int)score;

            _events.UpdateScore(clamped);
        }

        // Must be called by the host game every frame (e.g. from its bootstrap
        // MonoBehaviour's Update): the plugin has no "the ad never showed"
        // event, so stale requests are closed here. Public rather than internal
        // so tests can advance time explicitly.
        public void Tick()
        {
            float now = _now();

            // A pause from the portal means the ad really is on screen. While it
            // plays, the response deadline is pushed back: the timeout exists
            // for "no answer at all" (ad blocker, offline, silent portal), not
            // for "the player is honestly watching a long ad".
            //
            // This is exactly how rewards used to get lost: the game loop keeps
            // running during the show (timeScale = 0 does not stop rAF, and we
            // count realtimeSinceStartup), 30 seconds ran out mid-ad, the
            // request was closed as Failed — and the real success callback that
            // arrived after the ad had no one to go to.
            bool showing = _isAdShowing();

            if (_rewardedCallback != null)
            {
                if (showing && now < _rewardedExpiry)
                    _rewardedDeadline = now + RequestTimeoutSeconds;

                if (now > _rewardedDeadline)
                {
                    Debug.LogWarning("GamePixAdsProvider: the rewarded ad did not answer in time — treating it as not shown.");
                    AdsDebug.Record("REWARDED: TIMEOUT " + RequestTimeoutSeconds + "s - portal did not answer");
                    Resolve(ref _rewardedCallback, AdResult.Failed);
                }
            }

            if (_interstitialCallback != null)
            {
                if (showing && now < _interstitialExpiry)
                    _interstitialDeadline = now + RequestTimeoutSeconds;

                if (now > _interstitialDeadline)
                {
                    Debug.LogWarning("GamePixAdsProvider: the interstitial ad did not answer in time.");
                    AdsDebug.Record("INTERSTITIAL: TIMEOUT " + RequestTimeoutSeconds + "s - portal did not answer");
                    Resolve(ref _interstitialCallback, AdResult.Failed);
                }
            }
        }

        private void OnRewardedShown()
        {
            AdsDebug.Record("REWARDED: watched to the end -> Completed");
            Resolve(ref _rewardedCallback, AdResult.Completed);
        }

        // The plugin has a single onFail for both "closed early" and "no ad
        // available" — they cannot be told apart, so a generic Failed is reported.
        private void OnRewardedFailed()
        {
            AdsDebug.Record("REWARDED: portal refused -> Failed");
            Resolve(ref _rewardedCallback, AdResult.Failed);
        }

        private void OnInterstitialShown()
        {
            AdsDebug.Record("INTERSTITIAL: shown -> Completed");
            Resolve(ref _interstitialCallback, AdResult.Completed);
        }

        private void OnInterstitialFailed()
        {
            AdsDebug.Record("INTERSTITIAL: portal refused -> Failed");
            Resolve(ref _interstitialCallback, AdResult.Failed);
        }

        // The callback is cleared before it is invoked: if the plugin answers
        // again (or the answer arrives after the timeout), there is nothing left
        // to grant a second reward with.
        private static void Resolve(ref Action<AdResult> slot, AdResult result)
        {
            Action<AdResult> callback = slot;

            slot = null;

            callback?.Invoke(result);
        }
    }
}
