using GamePix;
using LongLeap.Core.Ads;
using UnityEngine;

namespace LongLeap.GamePix
{
    // The language the portal passed at launch. The plugin parses it from the
    // command-line arguments before the first scene loads, so the value is
    // ready by bootstrap time.
    public sealed class GamePixPortalLocale : IPortalLocale
    {
        public bool TryGetLocale(out string twoLetterCode)
        {
            twoLetterCode = ToTwoLetterCode(Gpx.CurrentLanguage);

            return twoLetterCode != null;
        }

        // The plugin reports a SystemLanguage. Convert it to ISO 639-1; the game
        // decides whether it supports that language (portal rules say: fall
        // back to English otherwise).
        private static string ToTwoLetterCode(SystemLanguage language)
        {
            switch (language)
            {
                case SystemLanguage.English: return "en";
                case SystemLanguage.Russian: return "ru";
                case SystemLanguage.German: return "de";
                case SystemLanguage.French: return "fr";
                case SystemLanguage.Spanish: return "es";
                case SystemLanguage.Italian: return "it";
                case SystemLanguage.Portuguese: return "pt";
                case SystemLanguage.Dutch: return "nl";
                case SystemLanguage.Turkish: return "tr";
                case SystemLanguage.Chinese: return "zh";
                case SystemLanguage.ChineseSimplified: return "zh";
                case SystemLanguage.ChineseTraditional: return "zh";
                case SystemLanguage.Korean: return "ko";
                case SystemLanguage.Japanese: return "ja";
                case SystemLanguage.Arabic: return "ar";
                default: return null;
            }
        }
    }
}
