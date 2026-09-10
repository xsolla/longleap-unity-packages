using System;
using GamePix;
using LongLeap.Core.Ads;
using UnityEngine;

namespace LongLeap.GamePix
{
    // Единственное место в проекте, которое трогает Gpx.Ads и Gpx.Events.
    // Вся сборка выключена defineConstraints "GAMEPIX", поэтому внутри #if не нужен.
    //
    // Вендорный IAds сам по себе для игры не годится, здесь к нему добавлено три вещи:
    //   • одиночный запрос — плагин молча игнорирует второй вызов до завершения
    //     первого («[GPX] Reward ad already called»), и без флага UI предлагал бы
    //     кнопку, нажатие которой ничего не делает и никак не объясняется;
    //   • таймаут — колбэк может не прийти вообще (блокировщик, offline);
    //   • одноразовая выдача — колбэк снимается до вызова, поэтому опоздавший
    //     ответ плагина не выдаст награду второй раз.
    public sealed class GamePixAdsProvider : IAdsProvider
    {
        // Сколько ждём, пока площадка ОТКЛИКНЕТСЯ на запрос. Это не длина
        // ролика: пока ролик идёт, срок отодвигается (см. Tick).
        private const float RequestTimeoutSeconds = 30f;

        // Абсолютный предел жизни запроса. Нужен на случай, когда площадка
        // поставила игру на паузу и не сняла её — например, вкладку свернули
        // посреди ролика и колбэк не придёт никогда. Без него запрос завис бы
        // навсегда и кнопка осталась бы недоступной до конца забега.
        private const float MaxRequestLifetimeSeconds = 600f;

        private readonly global::GamePix.Ads.IAds _ads;
        private readonly global::GamePix.Events.IEvents _events;
        private readonly Func<float> _now;
        private readonly Func<bool> _isAdShowing;

        private Action<AdResult> _rewardedCallback;
        private Action<AdResult> _interstitialCallback;

        private float _rewardedDeadline;
        private float _interstitialDeadline;

        // Момент, после которого запрос закрываем принудительно, что бы ни
        // говорила площадка.
        private float _rewardedExpiry;
        private float _interstitialExpiry;

        // Боевой конструктор: берём то, что подсунул плагин (в редакторе это его
        // же заглушки UnityAds/UnityEvents).
        public GamePixAdsProvider()
            : this(Gpx.Ads, Gpx.Events, () => Time.realtimeSinceStartup, () => Gpx.IsGamePaused)
        {
        }

        // Для тестов: вендорские заглушки в редакторе всегда отвечают успехом, так
        // что ни отказ, ни таймаут через них не проверить — нужна подмена.
        // Признак «ролик на экране» тоже подменяем: в редакторе Gpx.IsGamePaused
        // всегда false, и поведение во время показа иначе не проверить.
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

        // Провайдер существует только в портальной сборке — значит площадка есть.
        public bool IsAvailable => true;

        public bool IsRewardedReady => _rewardedCallback == null;

        public bool IsAdShowing => _isAdShowing();

        public void ShowRewarded(Action<AdResult> onComplete)
        {
            if (_rewardedCallback != null)
            {
                // Запрос уже в работе. Плагин всё равно проигнорирует вызов —
                // отвечаем сами, чтобы вызывающий код не ждал колбэка впустую.
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
            // У портала счёт int; забег столько не набирает, но обрезать молча нельзя.
            int clamped = score > int.MaxValue ? int.MaxValue : score < 0 ? 0 : (int)score;

            _events.UpdateScore(clamped);
        }

        // Вызывается из GamePixInstaller каждый кадр: у плагина нет события
        // «показ не состоялся», поэтому протухшие запросы закрываем сами.
        // Публичный, а не internal, чтобы тесты могли двигать время явно.
        public void Tick()
        {
            float now = _now();

            // Пауза от площадки означает, что ролик реально идёт на экране.
            // Пока он идёт, срок ответа отодвигаем: таймаут здесь стоит на
            // случай «ответа нет вообще» (блокировщик, offline, площадка молчит),
            // а не на случай «игрок честно смотрит длинный ролик».
            //
            // Ровно на этом терялась награда: игровой цикл во время показа
            // продолжает крутиться (timeScale=0 не останавливает rAF, а мы
            // считаем по realtimeSinceStartup), 30 секунд истекали посреди
            // ролика, запрос закрывался как Failed — и настоящий колбэк успеха,
            // пришедший после ролика, уже некому было отдать.
            bool showing = _isAdShowing();

            if (_rewardedCallback != null)
            {
                if (showing && now < _rewardedExpiry)
                    _rewardedDeadline = now + RequestTimeoutSeconds;

                if (now > _rewardedDeadline)
                {
                    Debug.LogWarning("GamePixAdsProvider: ролик за награду не ответил вовремя — считаем показ несостоявшимся.");
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
                    Debug.LogWarning("GamePixAdsProvider: межуровневая реклама не ответила вовремя.");
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

        // У плагина один onFail и на «закрыл раньше времени», и на «рекламы нет» —
        // различить их нельзя, поэтому наружу отдаём общий Failed.
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

        // Колбэк снимаем до вызова: если плагин ответит ещё раз (или ответ придёт
        // после таймаута), выдавать награду второй раз уже нечему.
        private static void Resolve(ref Action<AdResult> slot, AdResult result)
        {
            Action<AdResult> callback = slot;

            slot = null;

            callback?.Invoke(result);
        }
    }
}
