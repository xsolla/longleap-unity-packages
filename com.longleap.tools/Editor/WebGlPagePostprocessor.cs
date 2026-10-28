using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

// Класс не зависит от игры: живёт в пакете com.longleap.tools и работает в любом проекте.
// Режим страницы выбирается в меню Tools/WebGL Page Mode и применяется к следующему WebGL билду:
//   Fixed Aspect — пропорции берутся из размеров canvas в готовом index.html (WebGL Resolution в Player Settings),
//                  по бокам/сверху-снизу остаются поля;
//   Free Aspect  — canvas занимает всё окно браузера, любые пропорции.
// Ориентация игры выбирается там же, в Tools/WebGL Page Mode/Orientation:
//   Auto      — из WebGL Resolution в Player Settings: ширина < высоты — портретная, иначе альбомная;
//   Landscape / Portrait — явно.
// На сенсорных устройствах в неправильной ориентации игра закрывается заглушкой «поверните устройство».
// На ПК заглушки нет: окно браузера крутить нельзя, игра просто показывается с полями.
namespace LongLeap.Tools.Editor
{
    public static class WebGlPagePostprocessor
    {
        public enum PageMode
        {
            FixedAspect = 0,
            FreeAspect = 1,
        }

        public enum PageOrientation
        {
            Auto = 0,
            Landscape = 1,
            Portrait = 2,
        }

        private const string AspectPageMarker = "data-webgl-aspect-fit";
        private const string FreePageMarker = "data-webgl-free-aspect";
        private const string OrientationMarker = "data-webgl-orientation-lock";
        private const string MobileDpiMarker = "// config.devicePixelRatio = 1;";

        private const string MenuRoot = "Tools/WebGL Page Mode/";
        private const string FixedMenuPath = MenuRoot + "Fixed Aspect (Player Settings size)";
        private const string FreeMenuPath = MenuRoot + "Free Aspect (fill browser window)";
        private const string OrientationMenuRoot = MenuRoot + "Orientation/";
        private const string AutoOrientationMenuPath = OrientationMenuRoot + "Auto (Player Settings resolution)";
        private const string LandscapeMenuPath = OrientationMenuRoot + "Landscape";
        private const string PortraitMenuPath = OrientationMenuRoot + "Portrait";
        private const string ModePrefKey = "WebGlPagePostprocessor.Mode";
        private const string OrientationPrefKey = "WebGlPagePostprocessor.Orientation";

        // Настройки хранятся в EditorPrefs (локально у каждого разработчика, в репозиторий не попадают).
        private static string PrefKey => ModePrefKey + "." + PlayerSettings.productGUID;
        private static string OrientationKey => OrientationPrefKey + "." + PlayerSettings.productGUID;

        public static PageMode Mode
        {
            get => (PageMode)EditorPrefs.GetInt(PrefKey, (int)PageMode.FixedAspect);
            set => EditorPrefs.SetInt(PrefKey, (int)value);
        }

        public static PageOrientation Orientation
        {
            get => (PageOrientation)EditorPrefs.GetInt(OrientationKey, (int)PageOrientation.Auto);
            set => EditorPrefs.SetInt(OrientationKey, (int)value);
        }

        /// <summary>Портретная ли игра с учётом Auto: тогда решает WebGL Resolution в Player Settings.</summary>
        public static bool IsPortrait => Orientation switch
        {
            PageOrientation.Portrait => true,
            PageOrientation.Landscape => false,
            _ => PlayerSettings.defaultWebScreenWidth < PlayerSettings.defaultWebScreenHeight,
        };

        [MenuItem(FixedMenuPath, false, 2200)]
        private static void SelectFixed() => Mode = PageMode.FixedAspect;

        [MenuItem(FixedMenuPath, true)]
        private static bool SelectFixedValidate()
        {
            Menu.SetChecked(FixedMenuPath, Mode == PageMode.FixedAspect);
            return true;
        }

        [MenuItem(FreeMenuPath, false, 2201)]
        private static void SelectFree() => Mode = PageMode.FreeAspect;

        [MenuItem(FreeMenuPath, true)]
        private static bool SelectFreeValidate()
        {
            Menu.SetChecked(FreeMenuPath, Mode == PageMode.FreeAspect);
            return true;
        }

        [MenuItem(AutoOrientationMenuPath, false, 2300)]
        private static void SelectAutoOrientation() => Orientation = PageOrientation.Auto;

        [MenuItem(AutoOrientationMenuPath, true)]
        private static bool SelectAutoOrientationValidate()
        {
            Menu.SetChecked(AutoOrientationMenuPath, Orientation == PageOrientation.Auto);
            return true;
        }

        [MenuItem(LandscapeMenuPath, false, 2301)]
        private static void SelectLandscape() => Orientation = PageOrientation.Landscape;

        [MenuItem(LandscapeMenuPath, true)]
        private static bool SelectLandscapeValidate()
        {
            Menu.SetChecked(LandscapeMenuPath, Orientation == PageOrientation.Landscape);
            return true;
        }

        [MenuItem(PortraitMenuPath, false, 2302)]
        private static void SelectPortrait() => Orientation = PageOrientation.Portrait;

        [MenuItem(PortraitMenuPath, true)]
        private static bool SelectPortraitValidate()
        {
            Menu.SetChecked(PortraitMenuPath, Orientation == PageOrientation.Portrait);
            return true;
        }

        [PostProcessBuild(1000)]
        public static void ConfigurePage(BuildTarget target, string buildPath)
        {
            if (target != BuildTarget.WebGL)
                return;

            var indexPath = Path.Combine(buildPath, "index.html");
            if (!File.Exists(indexPath))
            {
                Debug.LogWarning($"WebGlPagePostprocessor: index.html not found at {indexPath}.");
                return;
            }

            var html = File.ReadAllText(indexPath);
            var changed = false;

            var mode = Mode;
            var alreadyStyled = html.IndexOf(AspectPageMarker, StringComparison.Ordinal) >= 0 ||
                                html.IndexOf(FreePageMarker, StringComparison.Ordinal) >= 0;

            if (mode == PageMode.FreeAspect && !alreadyStyled)
            {
                var headCloseIndex = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
                if (headCloseIndex >= 0)
                {
                    var background = "#" + ColorUtility.ToHtmlStringRGB(PlayerSettings.SplashScreen.backgroundColor);

                    // Unity по умолчанию подгоняет размер буфера под размер canvas (matchWebGLToCanvasSize),
                    // поэтому достаточно растянуть canvas на всё окно.
                    var pageStyle =
                        $"    <style {FreePageMarker}>\n" +
                        $"      html, body {{ width: 100%; height: 100%; margin: 0; padding: 0; border: 0; overflow: hidden; background: {background}; }}\n" +
                        "      #unity-container { position: fixed !important; left: 0 !important; top: 0 !important; transform: none !important;\n" +
                        "        width: 100% !important; height: 100% !important; }\n" +
                        "      #unity-canvas { position: fixed !important; left: 0 !important; top: 0 !important; transform: none !important;\n" +
                        "        width: 100% !important; height: 100% !important; display: block !important; }\n" +
                        "      #unity-footer { display: none !important; }\n" +
                        "    </style>\n";

                    html = html.Insert(headCloseIndex, pageStyle);
                    changed = true;
                    Debug.Log("WebGlPagePostprocessor: free aspect page (canvas fills the browser window).");
                }
                else
                {
                    Debug.LogWarning("WebGlPagePostprocessor: </head> not found in index.html.");
                }
            }
            else if (mode == PageMode.FixedAspect && !alreadyStyled)
            {
                var headCloseIndex = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
                var canvas = Regex.Match(html, "<canvas\\b[^>]*\\bid\\s*=\\s*[\"']unity-canvas[\"'][^>]*>",
                    RegexOptions.IgnoreCase);
                var width = Regex.Match(canvas.Value, "\\bwidth\\s*=\\s*[\"']?(\\d+)", RegexOptions.IgnoreCase);
                var height = Regex.Match(canvas.Value, "\\bheight\\s*=\\s*[\"']?(\\d+)", RegexOptions.IgnoreCase);

                if (headCloseIndex >= 0 &&
                    width.Success && height.Success &&
                    int.TryParse(width.Groups[1].Value, out var gameWidth) &&
                    int.TryParse(height.Groups[1].Value, out var gameHeight) &&
                    gameWidth > 0 && gameHeight > 0)
                {
                    // Размер canvas задаёт Unity из WebGL Player Settings.
                    var widthInVh = (100d * gameWidth / gameHeight).ToString("0.########", CultureInfo.InvariantCulture);
                    var heightInVw = (100d * gameHeight / gameWidth).ToString("0.########", CultureInfo.InvariantCulture);
                    var background = "#" + ColorUtility.ToHtmlStringRGB(PlayerSettings.SplashScreen.backgroundColor);
                    var pageStyle =
                        $"    <style {AspectPageMarker}>\n" +
                        $"      html, body {{ width: 100%; height: 100%; margin: 0; padding: 0; border: 0; overflow: hidden; background: {background}; }}\n" +
                        "      #unity-container { position: fixed !important; left: 50% !important; top: 50% !important; transform: translate(-50%, -50%) !important;\n" +
                        $"        width: min(100vw, {widthInVh}vh) !important; height: min(100vh, {heightInVw}vw) !important; }}\n" +
                        "      #unity-canvas { position: fixed !important; left: 50% !important; top: 50% !important; transform: translate(-50%, -50%) !important;\n" +
                        $"        width: min(100vw, {widthInVh}vh) !important; height: min(100vh, {heightInVw}vw) !important; display: block !important; }}\n" +
                        "      #unity-footer { display: none !important; }\n" +
                        "    </style>\n";

                    html = html.Insert(headCloseIndex, pageStyle);
                    changed = true;
                    Debug.Log($"WebGlPagePostprocessor: canvas aspect ratio set to {gameWidth}:{gameHeight}.");
                }
                else
                {
                    Debug.LogWarning("WebGlPagePostprocessor: canvas size or </head> not found in index.html.");
                }
            }

            // Ориентация обязательна в обоих режимах страницы.
            if (html.IndexOf(OrientationMarker, StringComparison.Ordinal) < 0)
            {
                var headCloseIndex = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
                var bodyCloseIndex = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);

                if (headCloseIndex >= 0 && bodyCloseIndex >= 0)
                {
                    var portrait = IsPortrait;
                    var background = "#" + ColorUtility.ToHtmlStringRGB(PlayerSettings.SplashScreen.backgroundColor);
                    var wrongOrientation = portrait ? "landscape" : "portrait";
                    var lockOrientation = portrait ? "portrait" : "landscape";
                    // Иконка телефона изначально вертикальная: анимация показывает поворот в нужную ориентацию.
                    var hintFrom = portrait ? "-90deg" : "0deg";
                    var hintTo = portrait ? "0deg" : "-90deg";

                    // Браузеры не дают надёжно заблокировать ориентацию (screen.orientation.lock работает
                    // только в fullscreen и не везде), поэтому в неправильной ориентации закрываем игру заглушкой.
                    // Заглушка только для сенсорных устройств (hover: none, pointer: coarse): окно браузера
                    // на ПК не повернуть, там игра просто показывается с полями.
                    var orientationStyle =
                        $"    <style {OrientationMarker}>\n" +
                        $"      #unity-rotate-overlay {{ display: none; position: fixed; left: 0; top: 0; width: 100%; height: 100%; z-index: 2147483647;\n" +
                        $"        background: {background}; color: #fff; font: 600 5vmin/1.3 sans-serif; text-align: center;\n" +
                        "        align-items: center; justify-content: center; flex-direction: column; }\n" +
                        "      #unity-rotate-overlay .icon { font-size: 18vmin; display: inline-block; animation: unity-rotate-hint 1.8s ease-in-out infinite; }\n" +
                        $"      @keyframes unity-rotate-hint {{ 0%, 20% {{ transform: rotate({hintFrom}); }} 60%, 100% {{ transform: rotate({hintTo}); }} }}\n" +
                        $"      @media (orientation: {wrongOrientation}) and (hover: none) and (pointer: coarse) {{\n" +
                        "        #unity-rotate-overlay { display: flex; }\n" +
                        "        #unity-container, #unity-canvas { visibility: hidden !important; }\n" +
                        "      }\n" +
                        "    </style>\n";

                    var overlay =
                        $"    <div id=\"unity-rotate-overlay\"><div class=\"icon\">&#128241;</div><div>Rotate your device to {lockOrientation}</div></div>\n" +
                        "    <script>\n" +
                        "      // Best effort: на Android в fullscreen ориентацию можно заблокировать.\n" +
                        "      document.addEventListener('fullscreenchange', function () {\n" +
                        "        if (document.fullscreenElement && screen.orientation && screen.orientation.lock)\n" +
                        $"          screen.orientation.lock('{lockOrientation}').catch(function () {{}});\n" +
                        "      });\n" +
                        "    </script>\n";

                    // Сначала body (он дальше по тексту), чтобы не сдвинуть индекс head.
                    html = html.Insert(bodyCloseIndex, overlay);
                    html = html.Insert(headCloseIndex, orientationStyle);
                    changed = true;
                    Debug.Log($"WebGlPagePostprocessor: {lockOrientation}-only overlay added (orientation: {Orientation}).");
                }
                else
                {
                    Debug.LogWarning("WebGlPagePostprocessor: </head> or </body> not found, orientation overlay skipped.");
                }
            }

            if (html.IndexOf(MobileDpiMarker, StringComparison.Ordinal) >= 0)
            {
                html = html.Replace(MobileDpiMarker, "config.devicePixelRatio = 1;");
                changed = true;
                Debug.Log("WebGlPagePostprocessor: mobile devicePixelRatio set to 1.");
            }

            if (changed)
                File.WriteAllText(indexPath, html);
        }
    }
}
