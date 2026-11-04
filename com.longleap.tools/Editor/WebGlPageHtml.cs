using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LongLeap.Tools.Editor
{
    /// <summary>
    /// Edits of a WebGL build's <c>index.html</c> that never touch Unity: a string and parameters
    /// in, a string out — which is what makes them testable. Every step leaves its own marker and
    /// is not repeated on an already processed page; <c>false</c> means the template did not fit
    /// (no <c>&lt;/head&gt;</c>, no canvas size, etc.). What to log is up to
    /// <see cref="WebGlPagePostprocessor"/>.
    /// </summary>
    public static class WebGlPageHtml
    {
        /// <summary>Attribute on the Fixed Aspect <c>&lt;style&gt;</c> tag.</summary>
        public const string AspectPageMarker = "data-webgl-aspect-fit";

        /// <summary>Attribute on the Free Aspect <c>&lt;style&gt;</c> tag.</summary>
        public const string FreePageMarker = "data-webgl-free-aspect";

        /// <summary>Attribute on the orientation overlay <c>&lt;style&gt;</c> tag.</summary>
        public const string OrientationMarker = "data-webgl-orientation-lock";

        /// <summary>The commented-out line of Unity's default template that <see cref="TrySetMobilePixelRatioOne"/> enables.</summary>
        public const string MobileDpiMarker = "// config.devicePixelRatio = 1;";

        private static readonly Regex CanvasTag = new Regex(
            "<canvas\\b[^>]*\\bid\\s*=\\s*[\"']unity-canvas[\"'][^>]*>", RegexOptions.IgnoreCase);

        private static readonly Regex WidthAttribute = new Regex(
            "\\bwidth\\s*=\\s*[\"']?(\\d+)", RegexOptions.IgnoreCase);

        private static readonly Regex HeightAttribute = new Regex(
            "\\bheight\\s*=\\s*[\"']?(\\d+)", RegexOptions.IgnoreCase);

        // Overlay text by browser language (first part of BCP 47: "pt-BR" -> "pt").
        // The first entry is the fallback: it stays in the markup if no browser language matches.
        private static readonly (string Language, string Landscape, string Portrait)[] RotateTexts =
        {
            ("en", "Rotate your device to landscape", "Rotate your device to portrait"),
            ("ru", "Поверните устройство горизонтально", "Поверните устройство вертикально"),
            ("uk", "Поверніть пристрій горизонтально", "Поверніть пристрій вертикально"),
            ("de", "Drehe dein Gerät ins Querformat", "Drehe dein Gerät ins Hochformat"),
            ("fr", "Tournez votre appareil en mode paysage", "Tournez votre appareil en mode portrait"),
            ("es", "Gira tu dispositivo a horizontal", "Gira tu dispositivo a vertical"),
            ("pt", "Gire o dispositivo para a horizontal", "Gire o dispositivo para a vertical"),
            ("it", "Ruota il dispositivo in orizzontale", "Ruota il dispositivo in verticale"),
            ("pl", "Obróć urządzenie poziomo", "Obróć urządzenie pionowo"),
            ("tr", "Cihazınızı yatay çevirin", "Cihazınızı dikey çevirin"),
            ("ja", "端末を横向きにしてください", "端末を縦向きにしてください"),
            ("ko", "기기를 가로로 돌려 주세요", "기기를 세로로 돌려 주세요"),
            ("zh", "请将设备横屏", "请将设备竖屏"),
        };

        /// <summary>A page style (Fixed or Free Aspect) has already been added.</summary>
        public static bool HasPageStyle(string html) =>
            Contains(html, AspectPageMarker) || Contains(html, FreePageMarker);

        /// <summary>Free Aspect: the canvas fills the whole browser window.</summary>
        public static bool TryAddFreeAspectStyle(ref string html, string background)
        {
            int headClose = IndexOfHeadClose(html);
            if (headClose < 0)
                return false;

            // By default Unity matches the backbuffer to the canvas size (matchWebGLToCanvasSize),
            // so stretching the canvas over the window is enough.
            string style =
                $"    <style {FreePageMarker}>\n" +
                PageBodyRule(background) +
                "      #unity-container { position: fixed !important; left: 0 !important; top: 0 !important; transform: none !important;\n" +
                "        width: 100% !important; height: 100% !important; }\n" +
                "      #unity-canvas { position: fixed !important; left: 0 !important; top: 0 !important; transform: none !important;\n" +
                "        width: 100% !important; height: 100% !important; display: block !important; }\n" +
                "      #unity-footer { display: none !important; }\n" +
                "    </style>\n";

            html = html.Insert(headClose, style);
            return true;
        }

        /// <summary>
        /// Fixed Aspect: the aspect ratio comes from the canvas size Unity writes into index.html
        /// from WebGL Resolution in Player Settings; margins remain around the game.
        /// </summary>
        public static bool TryAddFixedAspectStyle(ref string html, string background, out int width, out int height)
        {
            width = 0;
            height = 0;

            int headClose = IndexOfHeadClose(html);
            if (headClose < 0 || !TryReadCanvasSize(html, out width, out height))
                return false;

            string widthInVh = (100d * width / height).ToString("0.########", CultureInfo.InvariantCulture);
            string heightInVw = (100d * height / width).ToString("0.########", CultureInfo.InvariantCulture);

            string style =
                $"    <style {AspectPageMarker}>\n" +
                PageBodyRule(background) +
                "      #unity-container { position: fixed !important; left: 50% !important; top: 50% !important; transform: translate(-50%, -50%) !important;\n" +
                $"        width: min(100vw, {widthInVh}vh) !important; height: min(100vh, {heightInVw}vw) !important; }}\n" +
                "      #unity-canvas { position: fixed !important; left: 50% !important; top: 50% !important; transform: translate(-50%, -50%) !important;\n" +
                $"        width: min(100vw, {widthInVh}vh) !important; height: min(100vh, {heightInVw}vw) !important; display: block !important; }}\n" +
                "      #unity-footer { display: none !important; }\n" +
                "    </style>\n";

            html = html.Insert(headClose, style);
            return true;
        }

        /// <summary>Size from the <c>width</c>/<c>height</c> attributes of the <c>#unity-canvas</c> tag.</summary>
        public static bool TryReadCanvasSize(string html, out int width, out int height)
        {
            width = 0;
            height = 0;

            var canvas = CanvasTag.Match(html);
            if (!canvas.Success)
                return false;

            var widthMatch = WidthAttribute.Match(canvas.Value);
            var heightMatch = HeightAttribute.Match(canvas.Value);

            return widthMatch.Success && heightMatch.Success &&
                   int.TryParse(widthMatch.Groups[1].Value, out width) &&
                   int.TryParse(heightMatch.Groups[1].Value, out height) &&
                   width > 0 && height > 0;
        }

        /// <summary>The orientation overlay has already been added.</summary>
        public static bool HasOrientationOverlay(string html) => Contains(html, OrientationMarker);

        /// <summary>
        /// "Rotate your device" overlay for touch devices in the wrong orientation, plus a
        /// best-effort <c>screen.orientation.lock</c> in fullscreen. The text follows the browser language.
        /// </summary>
        public static bool TryAddOrientationOverlay(ref string html, bool portrait, string background)
        {
            int headClose = IndexOfHeadClose(html);
            int bodyClose = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            if (headClose < 0 || bodyClose < 0)
                return false;

            string wrongOrientation = portrait ? "landscape" : "portrait";
            string lockOrientation = portrait ? "portrait" : "landscape";
            // The phone icon starts upright: the animation shows the turn into the target orientation.
            string hintFrom = portrait ? "-90deg" : "0deg";
            string hintTo = portrait ? "0deg" : "-90deg";
            string fallbackText = portrait ? RotateTexts[0].Portrait : RotateTexts[0].Landscape;

            // Browsers cannot reliably lock orientation (screen.orientation.lock works only in
            // fullscreen and not everywhere), so in the wrong orientation the game is covered.
            // Touch devices only (hover: none, pointer: coarse): a desktop browser window cannot
            // be rotated, so there the game is simply shown with margins.
            string style =
                $"    <style {OrientationMarker}>\n" +
                "      #unity-rotate-overlay { display: none; position: fixed; left: 0; top: 0; width: 100%; height: 100%; z-index: 2147483647;\n" +
                $"        background: {background}; color: #fff; font: 600 5vmin/1.3 sans-serif; text-align: center;\n" +
                "        align-items: center; justify-content: center; flex-direction: column; }\n" +
                "      #unity-rotate-overlay .icon { font-size: 18vmin; display: inline-block; animation: unity-rotate-hint 1.8s ease-in-out infinite; }\n" +
                $"      @keyframes unity-rotate-hint {{ 0%, 20% {{ transform: rotate({hintFrom}); }} 60%, 100% {{ transform: rotate({hintTo}); }} }}\n" +
                $"      @media (orientation: {wrongOrientation}) and (hover: none) and (pointer: coarse) {{\n" +
                "        #unity-rotate-overlay { display: flex; }\n" +
                "        #unity-container, #unity-canvas { visibility: hidden !important; }\n" +
                "      }\n" +
                "    </style>\n";

            string overlay =
                $"    <div id=\"unity-rotate-overlay\"><div class=\"icon\">&#128241;</div><div class=\"text\">{fallbackText}</div></div>\n" +
                "    <script>\n" +
                "      (function () {\n" +
                "        // The page loads before Unity, so the overlay language is chosen here: the first\n" +
                "        // browser language that has a translation; otherwise English stays.\n" +
                $"        var texts = {RotateTextsJson(portrait)};\n" +
                "        var languages = navigator.languages && navigator.languages.length ? navigator.languages : [navigator.language || ''];\n" +
                "        for (var i = 0; i < languages.length; i++) {\n" +
                "          var code = String(languages[i]).toLowerCase().split('-')[0];\n" +
                "          if (texts.hasOwnProperty(code)) {\n" +
                "            document.querySelector('#unity-rotate-overlay .text').textContent = texts[code];\n" +
                "            break;\n" +
                "          }\n" +
                "        }\n" +
                "      })();\n" +
                "      // Best effort: on Android the orientation can be locked in fullscreen.\n" +
                "      document.addEventListener('fullscreenchange', function () {\n" +
                "        if (!document.fullscreenElement || !screen.orientation || !screen.orientation.lock) return;\n" +
                "        // Some browsers return nothing or throw instead of a rejected promise.\n" +
                "        try {\n" +
                $"          var locked = screen.orientation.lock('{lockOrientation}');\n" +
                "          if (locked && locked.catch) locked.catch(function () {});\n" +
                "        } catch (e) {}\n" +
                "      });\n" +
                "    </script>\n";

            // Body first (it is further down the text) so the head index does not shift.
            html = html.Insert(bodyClose, overlay);
            html = html.Insert(headClose, style);
            return true;
        }

        /// <summary>
        /// Uncomment <c>config.devicePixelRatio = 1</c> in the default template (it sits in the
        /// branch for mobile user agents). <c>false</c> — the line is missing or already uncommented.
        /// </summary>
        public static bool TrySetMobilePixelRatioOne(ref string html)
        {
            if (!Contains(html, MobileDpiMarker))
                return false;

            html = html.Replace(MobileDpiMarker, "config.devicePixelRatio = 1;");
            return true;
        }

        // Full-size html/body without margins or scrollbars, painted with the splash background.
        private static string PageBodyRule(string background) =>
            $"      html, body {{ width: 100%; height: 100%; margin: 0; padding: 0; border: 0; overflow: hidden; background: {background}; }}\n";

        private static int IndexOfHeadClose(string html) =>
            html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);

        private static bool Contains(string html, string value) =>
            html.IndexOf(value, StringComparison.Ordinal) >= 0;

        // JS object "language -> text" for the given orientation. Everything non-ASCII goes as
        // \uXXXX, so the script does not depend on the encoding the page is saved in.
        private static string RotateTextsJson(bool portrait)
        {
            var json = new StringBuilder("{ ");

            for (int i = 0; i < RotateTexts.Length; i++)
            {
                if (i > 0)
                    json.Append(", ");

                var entry = RotateTexts[i];
                json.Append('\'').Append(entry.Language).Append("': ");
                AppendJsString(json, portrait ? entry.Portrait : entry.Landscape);
            }

            return json.Append(" }").ToString();
        }

        // Single-quoted JS string literal with quotes, backslashes and non-ASCII escaped.
        private static void AppendJsString(StringBuilder json, string value)
        {
            json.Append('\'');

            foreach (char c in value)
            {
                if (c == '\'' || c == '\\')
                    json.Append('\\').Append(c);
                else if (c < 0x20 || c > 0x7e)
                    json.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else
                    json.Append(c);
            }

            json.Append('\'');
        }
    }
}
