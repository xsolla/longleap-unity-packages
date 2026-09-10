using System;
using System.Collections.Generic;
using LongLeap.Core.Ads;
using LongLeap.GamePix;
using NUnit.Framework;

// Проверяем ровно то, что вендорский плагин сам не гарантирует: одиночность
// запроса, таймаут и невозможность выдать награду дважды.
//
// Через настоящий Gpx это не проверить: в редакторе он подставляет UnityAds,
// который всегда немедленно отвечает успехом.
public sealed class GamePixAdsProviderTests
{
    // Заглушка вендорского IAds: колбэки не зовёт сама, их дёргает тест.
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

    // Плагин молча игнорирует второй вызов — провайдер обязан ответить сам,
    // иначе вызывающий код будет ждать колбэка, которого не будет.
    [Test]
    public void Rewarded_SecondRequestWhileInFlight_FailsImmediatelyAndDoesNotReachPlugin()
    {
        _provider.ShowRewarded(_ => { });

        var second = new List<AdResult>();
        _provider.ShowRewarded(second.Add);

        Assert.AreEqual(new[] { AdResult.Failed }, second);
        Assert.AreEqual(1, _ads.RewardCalls, "второй запрос не должен уходить в плагин");
    }

    [Test]
    public void Rewarded_WhileInFlight_IsNotReady()
    {
        Assert.IsTrue(_provider.IsRewardedReady);

        _provider.ShowRewarded(_ => { });
        Assert.IsFalse(_provider.IsRewardedReady, "кнопку надо гасить, пока идёт запрос");

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

    // Самое важное: опоздавший ответ плагина после таймаута не должен выдать
    // награду второй раз — иначе одно воскрешение превращается в два.
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

    // Регрессия по опубликованной сборке: игрок честно смотрел ролик, а награду
    // не получил. Пока ролик на экране, игровой цикл продолжает крутиться
    // (timeScale=0 не останавливает rAF, а срок мы считаем по реальному
    // времени), поэтому 30 секунд истекали посреди показа: запрос закрывался
    // как Failed, и настоящий колбэк успеха уже некому было отдать.
    [Test]
    public void Rewarded_AdOnScreenLongerThanTimeout_StillGrantsReward()
    {
        var results = new List<AdResult>();

        _provider.ShowRewarded(results.Add);

        // Площадка поставила игру на паузу — значит ролик реально идёт.
        _showing = true;

        for (_now = 1f; _now <= 120f; _now += 1f)
            _provider.Tick();

        Assert.IsEmpty(results, "пока ролик на экране, запрос закрывать нельзя");

        // Ролик кончился: площадка сняла паузу и ответила успехом.
        _showing = false;
        _ads.CompleteReward();

        Assert.AreEqual(new[] { AdResult.Completed }, results);
    }

    // Обратная сторона: если пауза от площадки не снялась (свернули вкладку
    // посреди ролика), запрос всё равно обязан закрыться — иначе IsRewardedReady
    // останется false и кнопка будет недоступна до конца забега.
    [Test]
    public void Rewarded_AdShowingForever_GivesUpAtLifetimeCap()
    {
        var results = new List<AdResult>();

        _provider.ShowRewarded(results.Add);
        _showing = true;

        for (_now = 1f; _now <= 700f; _now += 1f)
            _provider.Tick();

        Assert.AreEqual(new[] { AdResult.Failed }, results);
        Assert.IsTrue(_provider.IsRewardedReady, "запрос должен быть закрыт");
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
