#if GAMEPIX
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LongLeap.GamePix.Editor
{
    // Patches the portal build's index.html right after the build.
    //
    // Why a postprocess and not a custom WebGL template: the plugin forces
    // APPLICATION:Minimal (UnityConfigurator.SetPlayerSettings) on every build,
    // so any custom template gets overwritten. This hook is our code in the
    // package: a plugin update does not remove it, and it runs BEFORE the
    // Builder packs the html5 folder into the .gpx.
    //
    // Lives in the package, not in Assets: none of the fixes knows anything
    // about a particular game — they repair the vendor pipeline for any
    // LongLeap project that ships on GamePix.
    //
    // Does three things:
    //
    // 1. Fluid canvas — always. The Minimal template pins the canvas to a fixed
    //    "width: 960px; height: 600px" and cannot adapt to its container. On
    //    the portal the game lives in a foreign iframe (800x450 in the test
    //    toolkit), and whatever does not fit is cropped — buttons at the right
    //    and bottom edges included. Portal rules require the opposite: fit
    //    down to 640x480.
    //
    // 2. Title — "Unity Web Player | <Name>" becomes "<Name>" (see FixTitle).
    //
    // 3. SDK tag — always, as the first script in <head>. A mandatory item of
    //    the official GamePix guide ("Import the SDK inside your index.html
    //    MANDATORY ... as first script in the <head> section"). Without the
    //    window.GamePix object the plugin silently does not start (its glue has
    //    `typeof GamePix === "undefined"` → return), and the game shows an
    //    empty canvas.
    public sealed class GamePixIndexPostprocessor : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        // URL from the official GamePix guide.
        private const string SdkUrl = "https://integration.gamepix.com/sdk/v3/gamepix.sdk.js";

        private const string SdkTag = "<script src=\"" + SdkUrl + "\"></script>";

        // The old dev SDK this postprocess used to insert under the
        // GAMEPIX_INJECT_SDK define. Its nested API has no ad methods, so next
        // to the real SDK it only gets in the way — removed if found.
        private static readonly Regex LegacySdkTag = new Regex(
            @"[ \t]*<script src=""https?://gamepix\.blob\.core\.windows\.net/gpxlib/[^""]*""></script>\r?\n?",
            RegexOptions.IgnoreCase);

        private static readonly Regex HeadOpen = new Regex(@"<head(\s[^>]*)?>", RegexOptions.IgnoreCase);

        // The canvas is found by its element, NOT by a string with specific
        // pixels. A literal "width: 960px; height: 600px" used to be here, and
        // when the Player Settings resolution changed to 1920x1080 the fix
        // silently stopped applying: the canvas shipped pinned.
        private static readonly Regex CanvasStyle = new Regex(
            "(<canvas[^>]*?id=\"unity-canvas\"[^>]*?style=\")([^\"]*)(\")",
            RegexOptions.IgnoreCase);

        private const string FluidCanvasStyle =
            "width: 100%; height: 100%; display: block; background: #231F20";

        private const string HeadClose = "</head>";

        // Without a height on html/body the canvas percentages have nothing to
        // resolve against. Put into <head>, not before <body>: browsers tolerate
        // a style between </head> and <body>, but it is not valid markup, and
        // the portal's review looks at the built page.
        private const string FluidStyle =
            "    <style>html,body{width:100%;height:100%;margin:0;padding:0;border:0;" +
            "overflow:hidden;background:#231F20}</style>\n  ";

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.WebGL)
                return;

            string indexPath = Path.Combine(report.summary.outputPath, "index.html");

            if (!File.Exists(indexPath))
                return;

            string html = File.ReadAllText(indexPath);
            string original = html;

            html = MakeCanvasFluid(html);
            html = FixTitle(html);
            html = InjectSdkTag(html);

            if (html == original)
                return;

            File.WriteAllText(indexPath, html);
        }

        private static string MakeCanvasFluid(string html)
        {
            Match canvas = CanvasStyle.Match(html);

            if (!canvas.Success)
            {
                // Must not stay silent: that is exactly how the previous version
                // failed — it did not find its string and did not say so.
                Debug.LogWarning("GamePixIndexPostprocessor: canvas #unity-canvas not found in index.html — " +
                                 "the page was left as is. Did the WebGL template change?");
                return html;
            }

            string before = canvas.Groups[2].Value;

            if (before == FluidCanvasStyle)
                return html;

            html = CanvasStyle.Replace(html,
                m => m.Groups[1].Value + FluidCanvasStyle + m.Groups[3].Value, 1);

            if (!html.Contains("html,body{width:100%"))
            {
                if (html.Contains(HeadClose))
                    html = html.Replace(HeadClose, FluidStyle + HeadClose);
                else
                    Debug.LogWarning("GamePixIndexPostprocessor: index.html has no </head> — " +
                                     "the fluid html/body style was not inserted, the canvas may " +
                                     "end up with zero height.");
            }

            Debug.Log("GamePixIndexPostprocessor: the canvas was made fluid (was \"" + before +
                      "\") — the game fits the portal frame instead of being cropped at the edges.");

            return html;
        }

        // The template prints "Unity Web Player | <Name>". In an HTML5 upload the
        // portal serves our own page, so both the player and the review see
        // this title, and the rules require removing Unity's stock chrome and
        // keeping the game name the same everywhere.
        private static string FixTitle(string html)
        {
            var title = new Regex("<title>\\s*Unity Web Player\\s*\\|\\s*(.*?)</title>",
                RegexOptions.IgnoreCase);

            Match m = title.Match(html);

            if (!m.Success)
                return html;

            return title.Replace(html, "<title>" + m.Groups[1].Value.Trim() + "</title>", 1);
        }

        private static string InjectSdkTag(string html)
        {
            html = LegacySdkTag.Replace(html, string.Empty);

            Match head = HeadOpen.Match(html);

            if (!head.Success)
            {
                Debug.LogError("GamePixIndexPostprocessor: index.html has no <head> — " +
                               "the mandatory GamePix SDK tag was not inserted.");
                return html;
            }

            // The tag is already the first element of <head> — postprocessing
            // the same folder again changes nothing.
            int afterHead = head.Index + head.Length;
            if (html.Substring(afterHead).TrimStart().StartsWith(SdkTag))
                return html;

            // Present but not first — move it up: the guide requires the first script.
            html = html.Replace(SdkTag, string.Empty);
            html = html.Insert(afterHead, "\n    " + SdkTag);

            Debug.Log("GamePixIndexPostprocessor: the GamePix SDK tag was inserted as the first script in <head>.");
            return html;
        }
    }
}
#endif
