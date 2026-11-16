using LongLeap.Tools.Editor;
using NUnit.Framework;

namespace LongLeap.Tools.Tests
{
    /// <summary>index.html edits of the WebGL postprocessor, on a trimmed default template.</summary>
    public sealed class WebGlPageHtmlTests
    {
        // Unity's default template cut down to what the edits hook into.
        private const string Template =
            "<!DOCTYPE html>\n" +
            "<html lang=\"en-us\">\n" +
            "  <head>\n" +
            "    <meta charset=\"utf-8\">\n" +
            "    <title>Game</title>\n" +
            "  </head>\n" +
            "  <body>\n" +
            "    <div id=\"unity-container\" class=\"unity-desktop\">\n" +
            "      <canvas id=\"unity-canvas\" width=960 height=600 tabindex=\"-1\"></canvas>\n" +
            "    </div>\n" +
            "    <script>\n" +
            "      if (/iPhone|iPad|iPod|Android/i.test(navigator.userAgent)) {\n" +
            "        // config.devicePixelRatio = 1;\n" +
            "      }\n" +
            "    </script>\n" +
            "  </body>\n" +
            "</html>\n";

        private const string Background = "#123456";

        [Test]
        public void FixedAspect_UsesCanvasRatio()
        {
            string html = Template;

            Assert.IsTrue(WebGlPageHtml.TryAddFixedAspectStyle(ref html, Background, out int width, out int height));

            Assert.AreEqual(960, width);
            Assert.AreEqual(600, height);
            StringAssert.Contains("min(100vw, 160vh)", html);
            StringAssert.Contains("min(100vh, 62.5vw)", html);
            StringAssert.Contains("background: #123456", html);
            Assert.IsTrue(WebGlPageHtml.HasPageStyle(html));
            Assert.Less(html.IndexOf(WebGlPageHtml.AspectPageMarker), html.IndexOf("</head>"));
        }

        [Test]
        public void FixedAspect_WithoutCanvasSize_LeavesPageAlone()
        {
            string html = Template.Replace(" width=960 height=600", string.Empty);
            string original = html;

            Assert.IsFalse(WebGlPageHtml.TryAddFixedAspectStyle(ref html, Background, out _, out _));
            Assert.AreEqual(original, html);
        }

        [Test]
        public void CanvasSize_AcceptsQuotedAttributes()
        {
            string html = Template.Replace("width=960 height=600", "width=\"1080\" height='1920'");

            Assert.IsTrue(WebGlPageHtml.TryReadCanvasSize(html, out int width, out int height));
            Assert.AreEqual(1080, width);
            Assert.AreEqual(1920, height);
        }

        [Test]
        public void FreeAspect_StretchesCanvas()
        {
            string html = Template;

            Assert.IsTrue(WebGlPageHtml.TryAddFreeAspectStyle(ref html, Background));

            StringAssert.Contains(WebGlPageHtml.FreePageMarker, html);
            Assert.IsTrue(WebGlPageHtml.HasPageStyle(html));
        }

        [Test]
        public void FreeAspect_WithoutHead_Fails()
        {
            string html = Template.Replace("</head>", string.Empty);

            Assert.IsFalse(WebGlPageHtml.TryAddFreeAspectStyle(ref html, Background));
        }

        [Test]
        public void OrientationOverlay_Landscape_HidesGameInPortrait()
        {
            string html = Template;

            Assert.IsTrue(WebGlPageHtml.TryAddOrientationOverlay(ref html, portrait: false, Background));

            StringAssert.Contains("@media (orientation: portrait) and (hover: none) and (pointer: coarse)", html);
            StringAssert.Contains("screen.orientation.lock('landscape')", html);
            StringAssert.Contains("Rotate your device to landscape", html);
            Assert.IsTrue(WebGlPageHtml.HasOrientationOverlay(html));
            Assert.Less(html.IndexOf(WebGlPageHtml.OrientationMarker), html.IndexOf("</head>"));
            Assert.Less(html.IndexOf("unity-rotate-overlay\">"), html.IndexOf("</body>"));
        }

        [Test]
        public void OrientationOverlay_Portrait_HidesGameInLandscape()
        {
            string html = Template;

            Assert.IsTrue(WebGlPageHtml.TryAddOrientationOverlay(ref html, portrait: true, Background));

            StringAssert.Contains("@media (orientation: landscape)", html);
            StringAssert.Contains("screen.orientation.lock('portrait')", html);
            StringAssert.Contains("Rotate your device to portrait", html);
        }

        [Test]
        public void OrientationOverlay_TranslationsAreAsciiEscaped()
        {
            string html = Template;

            WebGlPageHtml.TryAddOrientationOverlay(ref html, portrait: false, Background);

            // U+041F (Cyrillic Pe) starts the Russian text — the script must not depend on the page encoding.
            StringAssert.Contains("'ru': '\\u041f", html);
            StringAssert.DoesNotContain("Поверните", html);
            StringAssert.Contains("navigator.languages", html);
        }

        [Test]
        public void OrientationOverlay_WithoutBody_LeavesPageAlone()
        {
            string html = Template.Replace("</body>", string.Empty);
            string original = html;

            Assert.IsFalse(WebGlPageHtml.TryAddOrientationOverlay(ref html, portrait: false, Background));
            Assert.AreEqual(original, html);
        }

        [Test]
        public void MobilePixelRatio_UncommentsOnce()
        {
            string html = Template;

            Assert.IsTrue(WebGlPageHtml.TrySetMobilePixelRatioOne(ref html));
            StringAssert.Contains("        config.devicePixelRatio = 1;", html);
            StringAssert.DoesNotContain(WebGlPageHtml.MobileDpiMarker, html);

            Assert.IsFalse(WebGlPageHtml.TrySetMobilePixelRatioOne(ref html));
        }
    }
}
