using System.Collections.Generic;
using LongLeap.Core.Editor;

namespace LongLeap.GamePix.Editor
{
    // The GamePix project options, shown both in the GamePix menu
    // (GamePixPluginMenu) and in the Build Manager window.
    internal sealed class GamePixOptions : IProjectOptionSectionProvider
    {
        public const string EnabledId = "gamepix.enabled";
        public const string ConstraintsId = "gamepix.constraints";
        public const string AnalyticsId = "gamepix.allow_analytics";
        public const string JsBridgeId = "gamepix.js_bridge";
        public const string AdDebugId = "gamepix.ad_debug";

        public const string DefineSymbol = GamePixAsmdefConstraints.DefineSymbol;
        public const string JsBridgeDefineSymbol = "GAMEPIX_JS";

        private const string AnalyticsDefineSymbol = "GAMEPIX_ALLOW_ANALYTICS";
        private const string AdDebugDefineSymbol = "GAMEPIX_AD_DEBUG";

        private const string PluginMissing =
            "Assets/Plugins/GamePix is missing — install the vendor plugin first. "
            + "With the folder gone, code under #if GAMEPIX would compile against an absent Gpx.";

        private const string NeedsGamePix =
            "GAMEPIX must be on — outside a portal build this define changes nothing.";

        public IEnumerable<ProjectOptionSection> GetSections()
        {
            yield return new ProjectOptionSection(
                "gamepix",
                "GamePix",
                10,
                new ProjectOption[]
                {
                    new DefineOption(
                        EnabledId,
                        DefineSymbol,
                        "Plugin Enabled (GAMEPIX)",
                        "The define that turns on the vendor portal plugin's assemblies. Do not " +
                        "turn it on without the Assets/Plugins/GamePix folder: code under " +
                        "#if GAMEPIX would compile against an absent Gpx and everything fails.",
                        required: true,
                        order: 200,
                        availability: PluginPresent,
                        enabledNote:
                        "GamePixOptions: GAMEPIX is on — the plugin assemblies appear after recompilation. "
                        + "Remember to turn it off before a regular WebGL build.",
                        disabledNote:
                        "GamePixOptions: GAMEPIX is off — the plugin assemblies disappear after recompilation."),

                    // Not a define: a one-way repair of two files on disk.
                    // Unticked — a plugin update wiped the constraints, applying
                    // puts them back; when everything is in place there is
                    // nothing to do and the row is disabled.
                    new CustomOption(
                        ConstraintsId,
                        "Plugin Has defineConstraints",
                        "Whether the plugin's asmdefs are constrained by GAMEPIX. Both plugin " +
                        "assemblies must be constrained: the runtime one so a regular WebGL build " +
                        "does not pull in its externs and auto-bootstrap, the editor one because " +
                        "Builder.cs uses ICSharpCode from the runtime assembly. A plugin update " +
                        "wipes these constraints.",
                        required: true,
                        order: 202,
                        read: _ => GamePixAsmdefConstraints.AreInPlace()
                            ? ProjectOption.On
                            : ProjectOption.Off,
                        write: (value, context) =>
                        {
                            if (value == ProjectOption.On)
                                GamePixAsmdefConstraints.Restore(context);
                        },
                        availability: PluginPresent,
                        oneWay: true),

                    // The package only provides the switch; the game reads the
                    // define and decides whether to start its analytics.
                    new DefineOption(
                        AnalyticsId,
                        AnalyticsDefineSymbol,
                        "Allow External Analytics (GA4)",
                        "External analytics (GA4) in the portal build. Off by default. The package " +
                        "only provides the define — game code must check GAMEPIX_ALLOW_ANALYTICS " +
                        "before starting its analytics.",
                        required: true,
                        order: 203,
                        availability: state =>
                            ProjectOptionAvailability.RequireDefine(state, DefineSymbol, NeedsGamePix),

                        confirmation: new ProjectOptionConfirmation(
                            "GamePix",
                            "Enable external analytics (GA4) in the portal build?",
                            "Enable",
                            "Cancel"),
                        enabledNote:
                        "GamePixOptions: external analytics allowed — GA4 starts in the portal build too.",
                        disabledNote:
                        "GamePixOptions: external analytics disallowed — GA4 does not start in the portal build."),

                    // Selects the Xsolla ID sign-in path: on — XsollaIdAuthJS
                    // (own jslib bridge), off — XsollaIdAuthGamePix (Gpx.User).
                    new DefineOption(
                        JsBridgeId,
                        JsBridgeDefineSymbol,
                        "Xsolla ID via JS Bridge (GAMEPIX_JS)",
                        "Compiles XsollaIdAuthJS, which reaches window.GamePix.user through the " +
                        "package's own jslib, instead of XsollaIdAuthGamePix, which needs the " +
                        "plugin's user API (Assets/Plugins/GamePix/User, Gpx.User). Turn it on " +
                        "only for a plugin without that API. Note: XsollaIdAuthJS waits with " +
                        "Task.Delay, which can hang in a WebGL player.",
                        required: false,
                        order: 205,
                        availability: state =>
                            ProjectOptionAvailability.RequireDefine(state, DefineSymbol, NeedsGamePix),
                        enabledNote:
                        "GamePixOptions: GAMEPIX_JS is on — Xsolla ID sign-in goes through XsollaIdAuthJS " +
                        "(jslib); XsollaIdAuthGamePix is not compiled.",
                        disabledNote:
                        "GamePixOptions: GAMEPIX_JS is off — Xsolla ID sign-in goes through XsollaIdAuthGamePix " +
                        "(Gpx.User); XsollaIdAuthJS is not compiled."),

                    // The package only provides the switch; the overlay itself
                    // lives in the game and reads AdsDebug.Lines (LongLeap.Core.Ads).
                    new DefineOption(
                        AdDebugId,
                        AdDebugDefineSymbol,
                        "Show Ad Debug Overlay",
                        "On-screen ad log: in the portal build the browser console of a foreign " +
                        "iframe is out of reach, but the outcome of an ad request must be visible. " +
                        "The package only provides the define — the game's overlay compiles under " +
                        "GAMEPIX_AD_DEBUG and shows AdsDebug.Lines. Useful in any build: outside " +
                        "the portal it shows Unavailable, which is an answer too.",
                        required: false,
                        order: 204,
                        enabledNote:
                        "GamePixOptions: on-screen ad log is on. "
                        + "Turn it off before submitting for review.",
                        disabledNote: "GamePixOptions: on-screen ad log is off."),
                });
        }

        private static ProjectOptionAvailability PluginPresent(IProjectOptionState state) =>
            GamePixAsmdefConstraints.IsPluginVisible()
                ? ProjectOptionAvailability.Available
                : ProjectOptionAvailability.Blocked(PluginMissing);
    }
}
