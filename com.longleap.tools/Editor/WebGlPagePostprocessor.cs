using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace LongLeap.Tools.Editor
{
    /// <summary>
    /// Adjusts <c>index.html</c> after every WebGL build. Game-agnostic: lives in
    /// com.longleap.tools and works in any project.
    ///
    /// <para>Page mode — menu <c>Tools/WebGL Page Mode</c>, applied to the next WebGL build:</para>
    /// <list type="bullet">
    /// <item><b>Fixed Aspect</b> — the aspect ratio comes from the canvas size in the built index.html
    /// (WebGL Resolution in Player Settings); margins remain at the sides or top/bottom.</item>
    /// <item><b>Free Aspect</b> — the canvas fills the whole browser window, any aspect ratio.</item>
    /// </list>
    ///
    /// <para>Orientation — <c>Tools/WebGL Page Mode/Orientation</c>:</para>
    /// <list type="bullet">
    /// <item><b>Auto</b> — from WebGL Resolution in Player Settings: width &lt; height is portrait, otherwise landscape.</item>
    /// <item><b>Landscape</b> / <b>Portrait</b> — explicit.</item>
    /// </list>
    /// On touch devices held in the wrong orientation the game is covered by a "rotate your device"
    /// overlay. Desktops get no overlay: a browser window cannot be rotated, so the game is just
    /// shown with margins.
    ///
    /// <para><c>Tools/WebGL Page Mode/Mobile Pixel Ratio 1</c> uncomments
    /// <c>config.devicePixelRatio = 1</c> in the template (faster on phones, but blurrier).</para>
    ///
    /// <para>All three settings live in <c>ProjectSettings/LongLeapWebGlPage.asset</c>
    /// (<see cref="WebGlPageSettings"/>) and are committed. The index.html edits themselves are in
    /// <see cref="WebGlPageHtml"/> (pure functions, covered by tests); this class only handles the
    /// file, the settings and the log.</para>
    /// </summary>
    public static class WebGlPagePostprocessor
    {
        /// <summary>How the game canvas is laid out on the page.</summary>
        public enum PageMode
        {
            /// <summary>Keep the Player Settings aspect ratio, fit into the window with margins.</summary>
            FixedAspect = 0,

            /// <summary>Stretch the canvas over the whole browser window.</summary>
            FreeAspect = 1,
        }

        /// <summary>The orientation the game is designed for.</summary>
        public enum PageOrientation
        {
            /// <summary>Derive from WebGL Resolution in Player Settings.</summary>
            Auto = 0,

            /// <summary>Wider than tall.</summary>
            Landscape = 1,

            /// <summary>Taller than wide.</summary>
            Portrait = 2,
        }

        private const string MenuRoot = "Tools/WebGL Page Mode/";
        private const string FixedMenuPath = MenuRoot + "Fixed Aspect (Player Settings size)";
        private const string FreeMenuPath = MenuRoot + "Free Aspect (fill browser window)";
        private const string OrientationMenuRoot = MenuRoot + "Orientation/";
        private const string AutoOrientationMenuPath = OrientationMenuRoot + "Auto (Player Settings resolution)";
        private const string LandscapeMenuPath = OrientationMenuRoot + "Landscape";
        private const string PortraitMenuPath = OrientationMenuRoot + "Portrait";
        private const string MobileDpiMenuPath = MenuRoot + "Mobile Pixel Ratio 1 (faster, lower resolution)";

        /// <summary>Page mode for the next build. Project setting, shared by all developers and CI.</summary>
        public static PageMode Mode
        {
            get => WebGlPageSettings.Get().Mode;
            set => WebGlPageSettings.Get().Mode = value;
        }

        /// <summary>Game orientation for the next build. Project setting, shared by all developers and CI.</summary>
        public static PageOrientation Orientation
        {
            get => WebGlPageSettings.Get().Orientation;
            set => WebGlPageSettings.Get().Orientation = value;
        }

        /// <summary>Force <c>devicePixelRatio = 1</c> on mobile browsers. See <see cref="WebGlPageSettings.MobilePixelRatioOne"/>.</summary>
        public static bool MobilePixelRatioOne
        {
            get => WebGlPageSettings.Get().MobilePixelRatioOne;
            set => WebGlPageSettings.Get().MobilePixelRatioOne = value;
        }

        /// <summary>Whether the game is portrait, resolving Auto: then WebGL Resolution in Player Settings decides.</summary>
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

        [MenuItem(MobileDpiMenuPath, false, 2400)]
        private static void ToggleMobileDpi() => MobilePixelRatioOne = !MobilePixelRatioOne;

        [MenuItem(MobileDpiMenuPath, true)]
        private static bool ToggleMobileDpiValidate()
        {
            Menu.SetChecked(MobileDpiMenuPath, MobilePixelRatioOne);
            return true;
        }

        /// <summary>
        /// Post-build hook: applies the page style, the orientation overlay and the mobile pixel
        /// ratio to <c>index.html</c> of a WebGL build. Runs after the other index.html
        /// postprocessors (order 1000); each step is skipped if the page already has it.
        /// </summary>
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
            var original = html;
            var background = "#" + ColorUtility.ToHtmlStringRGB(PlayerSettings.SplashScreen.backgroundColor);

            AddPageStyle(ref html, background);
            AddOrientationOverlay(ref html, background);

            if (MobilePixelRatioOne && WebGlPageHtml.TrySetMobilePixelRatioOne(ref html))
                Debug.Log("WebGlPagePostprocessor: mobile devicePixelRatio set to 1.");

            if (html != original)
                File.WriteAllText(indexPath, html);
        }

        // Fixed or Free Aspect style, depending on Mode.
        private static void AddPageStyle(ref string html, string background)
        {
            if (WebGlPageHtml.HasPageStyle(html))
                return;

            if (Mode == PageMode.FreeAspect)
            {
                if (WebGlPageHtml.TryAddFreeAspectStyle(ref html, background))
                    Debug.Log("WebGlPagePostprocessor: free aspect page (canvas fills the browser window).");
                else
                    Debug.LogWarning("WebGlPagePostprocessor: </head> not found in index.html.");

                return;
            }

            if (WebGlPageHtml.TryAddFixedAspectStyle(ref html, background, out var width, out var height))
                Debug.Log($"WebGlPagePostprocessor: canvas aspect ratio set to {width}:{height}.");
            else
                Debug.LogWarning("WebGlPagePostprocessor: canvas size or </head> not found in index.html.");
        }

        // The orientation overlay is needed in both page modes.
        private static void AddOrientationOverlay(ref string html, string background)
        {
            if (WebGlPageHtml.HasOrientationOverlay(html))
                return;

            var portrait = IsPortrait;

            if (WebGlPageHtml.TryAddOrientationOverlay(ref html, portrait, background))
                Debug.Log($"WebGlPagePostprocessor: {(portrait ? "portrait" : "landscape")}-only overlay added " +
                          $"(orientation: {Orientation}).");
            else
                Debug.LogWarning("WebGlPagePostprocessor: </head> or </body> not found, orientation overlay skipped.");
        }
    }
}
