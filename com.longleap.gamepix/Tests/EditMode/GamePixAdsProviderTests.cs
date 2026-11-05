using System;
using System.Collections.Generic;
using LongLeap.Core.Ads;
using LongLeap.GamePix;
using NUnit.Framework;

// Tests exactly what the vendor plugin does not guarantee itself: a single
// request at a time, the timeout, and that a reward cannot be granted twice.
//
// This cannot be tested through the real Gpx: in the Editor it substitutes
// UnityAds, which always answers with success immediately.
public sealed class GamePixAdsProviderTests
{
    // Fake vendor IAds: never calls the callbacks itself, the test triggers them.
    private sealed class FakeAds : global::GamePix.Ads.IAds
    {
        public int RewardCalls;
        public int InterstitialCalls;

        public void InterstitialAd(Action onSuccess = null, Action onFail = null)
        {
            InterstitialCalls++;
        }

        public void RewardAd(Action onSuccess, Action onFail = null, bool prompt = false)
        {
            RewardCalls++;
            _rewardSuccess = onSuccess;
            _rewardFail = onFail;
        }

        private Action _rewardSuccess;
        private Action _rewardFail;

        public void CompleteReward() => _rewardSuccess?.Invoke();

        public void FailReward() => _rewardFail?.Invoke();
    }

    private sealed class FakeEvents : global::GamePix.Events.IEvents
    {
        public readonly List<int> Scores = new();

        public void UpdateScore(int score) => Scores.Add(score);

        public void HappyMoment() { }

        public void SadMoment() { }
    }

    private FakeAds _ads;
    private FakeEvents _events;
    private float _now;
    private bool _showing;
    private GamePixAdsProvider _provider;

    [SetUp]
    public void SetUp()
    {
        _ads = new FakeAds();
        _events = new FakeEvents();
        _now = 0f;
        _showing = false;
        _provider = new GamePixAdsProvider(_ads, _events, () => _now, () => _showing);
    }

    [Test]
    public void Rewarded_Completed_ReportsCompletedOnce()
    {
        var results = new List<AdResult>();

        _provider.ShowRewarded(results.Add);
        _ads.CompleteReward();

        Assert.AreEqual(new[] { AdResult.Completed }, results);
    }

    [Test]
    public void Rewarded_Failed_ReportsFailed()
    {
        var results = new List<AdResult>();

        _provider.ShowRewarded(results.Add);
        _ads.FailReward();

        Assert.AreEqual(new[] { AdResult.Failed }, results);
    }

    // The plugin silently ignores a second call — the provider must answer
    // itself, otherwise the caller waits for a callback that never comes.
    [Test]
    public void Rewarded_SecondRequestWhileInFlight_FailsImmediatelyAndDoesNotReachPlugin()
    {
        _provider.ShowRewarded(_ => { });

        var second = new List<AdResult>();
        _provider.ShowRewarded(second.Add);

        Assert.AreEqual(new[] { AdResult.Failed }, second);
        Assert.AreEqual(1, _ads.RewardCalls, "the second request must not reach the plugin");
    }

    [Test]
    public void Rewarded_WhileInFlight_IsNotReady()
    {
        Assert.IsTrue(_provider.IsRewardedReady);

        _provider.ShowRewarded(_ => { });
        Assert.IsFalse(_provider.IsRewardedReady, "the button must be disabled while a request is in flight");

        _ads.CompleteReward();
        Assert.IsTrue(_provider.IsRewardedReady);
    }

    [Test]
    public void Rewarded_NoAnswer_TimesOutAsFailedExactlyOnce()
    {
        var results = new List<AdResult>();

        _provider.ShowRewarded(results.Add);

        _now = 31f;
        _provider.Tick();
        _provider.Tick();

        Assert.AreEqual(new[] { AdResult.Failed }, results);
    }

    // The most important one: a late plugin answer after the timeout must not
    // grant the reward a second time — otherwise one revive turns into two.
    [Test]
    public void Rewarded_LateAnswerAfterTimeout_DoesNotGrantTwice()
    {
        var results = new List<AdResult>();

        _provider.ShowRewarded(results.Add);

        _now = 31f;
        _provider.Tick();

        _ads.CompleteReward();

        Assert.AreEqual(new[] { AdResult.Failed }, results);
    }

    // Regression from a published build: the player honestly watched the ad but
    // got no reward. While the ad is on screen the game loop keeps running
    // (timeScale = 0 does not stop rAF, and the deadline is measured in real
    // time), so 30 seconds ran out mid-show: the request was closed as Failed,
    // and the real success callback had no one to go to.
    [Test]
    public void Rewarded_AdOnScreenLongerThanTimeout_StillGrantsReward()
    {
        var results = new List<AdResult>();

        _provider.ShowRewarded(results.Add);

        // The portal paused the game — so the ad really is playing.
        _showing = true;

        for (_now = 1f; _now <= 120f; _now += 1f)
            _provider.Tick();

        Assert.IsEmpty(results, "the request must not be closed while the ad is on screen");

        // The ad ended: the portal unpaused the game and answered with success.
        _showing = false;
        _ads.CompleteReward();

        Assert.AreEqual(new[] { AdResult.Completed }, results);
    }

    // The flip side: if the portal's pause never lifts (the tab was minimised
    // mid-ad), the request must still close — otherwise IsRewardedReady stays
    // false and the button is unavailable for the rest of the run.
    [Test]
    public void Rewarded_AdShowingForever_GivesUpAtLifetimeCap()
    {
        var results = new List<AdResult>();

        _provider.ShowRewarded(results.Add);
        _showing = true;

        for (_now = 1f; _now <= 700f; _now += 1f)
            _provider.Tick();

        Assert.AreEqual(new[] { AdResult.Failed }, results);
        Assert.IsTrue(_provider.IsRewardedReady, "the request must be closed");
    }

    [Test]
    public void ReportScore_ClampsToPortalRange()
    {
        _provider.ReportScore(-5);
        _provider.ReportScore(123);
        _provider.ReportScore(long.MaxValue);

        Assert.AreEqual(new[] { 0, 123, int.MaxValue }, _events.Scores);
    }
}
