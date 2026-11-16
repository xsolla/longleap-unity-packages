using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace LongLeap.Analytics
{
    // The request-level "device" object of the Measurement Protocol. GA4 does
    // not derive device data for Measurement Protocol traffic, so without it
    // platform, OS, language and screen resolution show as "(not set)".
    //
    //     backend.Device = Ga4DeviceInfo.FromCurrentPlatform();
    //
    // Every field is optional; empty ones are not sent.
    public sealed class Ga4DeviceInfo
    {
        private static readonly Regex VersionPattern = new Regex(@"\d+(?:[._]\d+)*", RegexOptions.Compiled);

        // "desktop", "mobile", "tablet" or "smart tv".
        public string Category { get; set; }

        // ISO 639-1 code, optionally with region: "en", "pt-br".
        public string Language { get; set; }

        // "1920x1080".
        public string ScreenResolution { get; set; }

        // "Windows", "Macintosh", "Linux", "iOS", "Android", "Chrome OS".
        public string OperatingSystem { get; set; }

        public string OperatingSystemVersion { get; set; }

        public string Model { get; set; }

        public string Brand { get; set; }

        public string Browser { get; set; }

        public string BrowserVersion { get; set; }

        // Reads what Unity knows about the device. Call on the main thread.
        // Browser and browser version are not available to Unity on WebGL;
        // set them yourself if you need them.
        public static Ga4DeviceInfo FromCurrentPlatform()
        {
            var info = new Ga4DeviceInfo
            {
                Category = DetectCategory(),
                Language = LanguageCode(Application.systemLanguage),
                OperatingSystem = DetectOperatingSystem(SystemInfo.operatingSystem),
            };

            var resolution = Screen.currentResolution;
            if (resolution.width > 0 && resolution.height > 0)
                info.ScreenResolution = resolution.width + "x" + resolution.height;

            var version = VersionPattern.Match(SystemInfo.operatingSystem ?? string.Empty);
            if (version.Success)
                info.OperatingSystemVersion = version.Value.Replace('_', '.');

            // Desktop "models" are motherboard strings ("System Product Name");
            // WebGL has none. Only native mobile models are meaningful.
            if (Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.IPhonePlayer)
            {
                var model = SystemInfo.deviceModel;
                if (!string.IsNullOrEmpty(model) && model != SystemInfo.unsupportedIdentifier)
                {
                    if (Application.platform == RuntimePlatform.IPhonePlayer)
                    {
                        info.Brand = "Apple";
                        info.Model = model;
                    }
                    else
                    {
                        // Android reports "<manufacturer> <model>".
                        var space = model.IndexOf(' ');
                        info.Brand = space > 0 ? model.Substring(0, space) : null;
                        info.Model = space > 0 ? model.Substring(space + 1) : model;
                    }
                }
            }

            return info;
        }

        internal Dictionary<string, object> ToJson()
        {
            var json = new Dictionary<string, object>();
            Add(json, "category", Category);
            Add(json, "language", Language);
            Add(json, "screen_resolution", ScreenResolution);
            Add(json, "operating_system", OperatingSystem);
            Add(json, "operating_system_version", OperatingSystemVersion);
            Add(json, "model", Model);
            Add(json, "brand", Brand);
            Add(json, "browser", Browser);
            Add(json, "browser_version", BrowserVersion);
            return json;
        }

        private static void Add(Dictionary<string, object> json, string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                json[key] = value;
        }

        private static string DetectCategory()
        {
            if (Application.isMobilePlatform || SystemInfo.deviceType == DeviceType.Handheld)
            {
                // Unity has no tablet flag: use the physical screen diagonal.
                var dpi = Screen.dpi;
                if (dpi > 0f)
                {
                    var width = Screen.width / dpi;
                    var height = Screen.height / dpi;
                    if (Mathf.Sqrt(width * width + height * height) >= 7f)
                        return "tablet";
                }

                return "mobile";
            }

            return SystemInfo.deviceType == DeviceType.Desktop ? "desktop" : null;
        }

        private static string DetectOperatingSystem(string description)
        {
            switch (Application.platform)
            {
                case RuntimePlatform.Android: return "Android";
                case RuntimePlatform.IPhonePlayer: return "iOS";
                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.WindowsEditor: return "Windows";
                case RuntimePlatform.OSXPlayer:
                case RuntimePlatform.OSXEditor: return "Macintosh";
                case RuntimePlatform.LinuxPlayer:
                case RuntimePlatform.LinuxEditor: return "Linux";
            }

            // WebGL: Unity derives operatingSystem from the browser's user agent.
            var text = description ?? string.Empty;
            if (text.Contains("Android")) return "Android";
            if (text.Contains("iOS") || text.Contains("iPhone") || text.Contains("iPad")) return "iOS";
            if (text.Contains("Windows")) return "Windows";
            if (text.Contains("Mac")) return "Macintosh";
            if (text.Contains("CrOS") || text.Contains("Chrome OS")) return "Chrome OS";
            if (text.Contains("Linux")) return "Linux";
            return null;
        }

        private static string LanguageCode(SystemLanguage language)
        {
            switch (language)
            {
                case SystemLanguage.Afrikaans: return "af";
                case SystemLanguage.Arabic: return "ar";
                case SystemLanguage.Basque: return "eu";
                case SystemLanguage.Belarusian: return "be";
                case SystemLanguage.Bulgarian: return "bg";
                case SystemLanguage.Catalan: return "ca";
                case SystemLanguage.Chinese:
                case SystemLanguage.ChineseSimplified: return "zh-cn";
                case SystemLanguage.ChineseTraditional: return "zh-tw";
                case SystemLanguage.Czech: return "cs";
                case SystemLanguage.Danish: return "da";
                case SystemLanguage.Dutch: return "nl";
                case SystemLanguage.English: return "en";
                case SystemLanguage.Estonian: return "et";
                case SystemLanguage.Faroese: return "fo";
                case SystemLanguage.Finnish: return "fi";
                case SystemLanguage.French: return "fr";
                case SystemLanguage.German: return "de";
                case SystemLanguage.Greek: return "el";
                case SystemLanguage.Hebrew: return "he";
                case SystemLanguage.Hindi: return "hi";
                case SystemLanguage.Hungarian: return "hu";
                case SystemLanguage.Icelandic: return "is";
                case SystemLanguage.Indonesian: return "id";
                case SystemLanguage.Italian: return "it";
                case SystemLanguage.Japanese: return "ja";
                case SystemLanguage.Korean: return "ko";
                case SystemLanguage.Latvian: return "lv";
                case SystemLanguage.Lithuanian: return "lt";
                case SystemLanguage.Norwegian: return "no";
                case SystemLanguage.Polish: return "pl";
                case SystemLanguage.Portuguese: return "pt";
                case SystemLanguage.Romanian: return "ro";
                case SystemLanguage.Russian: return "ru";
                case SystemLanguage.SerboCroatian: return "sh";
                case SystemLanguage.Slovak: return "sk";
                case SystemLanguage.Slovenian: return "sl";
                case SystemLanguage.Spanish: return "es";
                case SystemLanguage.Swedish: return "sv";
                case SystemLanguage.Thai: return "th";
                case SystemLanguage.Turkish: return "tr";
                case SystemLanguage.Ukrainian: return "uk";
                case SystemLanguage.Vietnamese: return "vi";
                default: return null;
            }
        }
    }
}
