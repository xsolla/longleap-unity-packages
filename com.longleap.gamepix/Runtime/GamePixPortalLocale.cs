using System;
using GamePix;
using LongLeap.Core.Ads;
using UnityEngine;

namespace LongLeap.GamePix
{
    // Язык, который портал передал при запуске. Плагин разбирает его из аргументов
    // командной строки ещё до загрузки сцены, так что значение готово к бутстрапу.
    public sealed class GamePixPortalLocale : IPortalLocale
    {
        public bool TryGetLocale(out string twoLetterCode)
        {
            twoLetterCode = ToTwoLetterCode(Gpx.CurrentLanguage);

            return twoLetterCode != null;
        }

        // Плагин отдаёт SystemLanguage. Переводим в ISO-639-1, игра дальше сама
        // решает, поддерживает ли такой язык (иначе по правилам портала — английский).
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
