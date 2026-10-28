using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

// Класс не зависит от игры: живёт в пакете com.longleap.tools и работает в любом проекте.
// Режим страницы выбирается в меню Tools/WebGL Page Mode и применяется к следующему WebGL билду:
//   Fixed Aspect — пропорции берутся из размеров canvas в готовом index.html (у нас 1920x1080),
//                  по бокам/сверху-снизу остаются поля;
//   Free Aspect  — canvas занимает всё окно браузера, любые пропорции.
namespace LongLeap.Tools.Editor
{
    public static class WebGlPagePostprocessor
    {
        public enum PageMode
        {
            FixedAspect = 0,
            FreeAspect = 1,
        }

        private const string AspectPageMarker = "data-webgl-aspect-fit";
        private const string FreePageMarker = "data-webgl-free-aspect";
        private const string LandscapeMarker = "data-webgl-landscape-only";
        private const string MobileDpiMarker = "// config.devicePixelRatio = 1;";

        private const string MenuRoot = "Tools/WebGL Page Mode/";
        private const string FixedMenuPath = MenuRoot + "Fixed Aspect (Player Settings size)";
        private const string FreeMenuPath = MenuRoot + "Free Aspect (fill browser window)";
        private const string ModePrefKey = "WebGlPagePostprocessor.Mode";

        // Настройка хранится в EditorPrefs (локально у каждого разработчика, в репозиторий не попадает).
        private static string PrefKey => ModePrefKey + "." + PlayerSettings.productGUID;

        public static PageMode Mode
        {
            get => (PageMode)EditorPrefs.GetInt(PrefKey, (int)PageMode.FixedAspect);
            set => EditorPrefs.SetInt(PrefKey, (int)value);
        }

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

            // Альбомная ориентация обязательна в обоих режимах.
            if (html.IndexOf(LandscapeMarker, StringComparison.Ordinal) < 0)
            {
                var headCloseIndex = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
                var bodyCloseIndex = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);

                if (headCloseIndex >= 0 && bodyCloseIndex >= 0)
                {
                    var background = "#" + ColorUtility.ToHtmlStringRGB(PlayerSettings.SplashScreen.backgroundColor);

                    // Браузеры не дают надёжно заблокировать ориентацию (screen.orientation.lock работает
                    // только в fullscreen и не везде), поэтому в портрете закрываем игру заглушкой.
                    var landscapeStyle =
                        $"    <style {LandscapeMarker}>\n" +
                        $"      #unity-rotate-overlay {{ display: none; position: fixed; left: 0; top: 0; width: 100%; height: 100%; z-index: 2147483647;\n" +
                        $"        background: {background}; color: #fff; font: 600 5vmin/1.3 sans-serif; text-align: center;\n" +
                        "        align-items: center; justify-content: center; flex-direction: column; }\n" +
                        "      #unity-rotate-overlay .icon { font-size: 18vmin; display: inline-block; animation: unity-rotate-hint 1.8s ease-in-out infinite; }\n" +
                        "      @keyframes unity-rotate-hint { 0%, 20% { transform: rotate(0deg); } 60%, 100% { transform: rotate(-90deg); } }\n" +
                        "      @media (orientation: portrait) {\n" +
                        "        #unity-rotate-overlay { display: flex; }\n" +
                        "        #unity-container, #unity-canvas { visibility: hidden !important; }\n" +
                        "      }\n" +
                        "    </style>\n";

                    const string overlay =
                        "    <div id=\"unity-rotate-overlay\"><div class=\"icon\">&#128241;</div><div>Rotate your device to landscape</div></div>\n" +
                        "    <script>\n" +
                        "      // Best effort: на Android в fullscreen ориентацию можно заблокировать.\n" +
                        "      document.addEventListener('fullscreenchange', function () {\n" +
                        "        if (document.fullscreenElement && screen.orientation && screen.orientation.lock)\n" +
                        "          screen.orientation.lock('landscape').catch(function () {});\n" +
                        "      });\n" +
                        "    </script>\n";

                    // Сначала body (он дальше по тексту), чтобы не сдвинуть индекс head.
                    html = html.Insert(bodyCloseIndex, overlay);
                    html = html.Insert(headCloseIndex, landscapeStyle);
                    changed = true;
                    Debug.Log("WebGlPagePostprocessor: landscape-only overlay added.");
                }
                else
                {
                    Debug.LogWarning("WebGlPagePostprocessor: </head> or </body> not found, landscape overlay skipped.");
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
