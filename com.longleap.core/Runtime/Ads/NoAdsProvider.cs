using System;

namespace LongLeap.Core.Ads
{
    // Заглушка для непортальной сборки: рекламы нет и не будет.
    //
    // Возвращать её вместо null — сознательно: вызывающий код тогда не обрастает
    // null-проверками, а честно получает «нельзя» на каждый вопрос.
    public sealed class NoAdsProvider : IAdsProvider
    {
        public bool IsAvailable => false;

        public bool IsRewardedReady => false;

        public bool IsAdShowing => false;

        public void ShowRewarded(Action<AdResult> onComplete)
        {
            // Пишем и это: на экране сразу видно, что сборка непортальная, а не
            // что реклама «сломалась».
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

    // Заглушка языка: портала нет, работает обычная настройка из сохранений.
    public sealed class NoPortalLocale : IPortalLocale
    {
        public bool TryGetLocale(out string twoLetterCode)
        {
            twoLetterCode = null;

            return false;
        }
    }
}
