using LongLeap.BuildManager.Options;
using UnityEditor;

namespace LongLeap.GamePix.Editor
{
    // The GamePix menu toggles. The Required submenu holds what a portal build
    // cannot be built or pass review without; Optional holds the rest.
    //
    //   Required/Plugin Enabled (GAMEPIX)            — the define that turns on the plugin assemblies;
    //   Required/Plugin Has defineConstraints        — whether the plugin's asmdefs carry the GAMEPIX
    //     constraint. Unticked — a plugin update wiped it, clicking restores it; when everything is
    //     in place the item is disabled;
    //   Required/Allow External Analytics (GA4)      — the GAMEPIX_ALLOW_ANALYTICS define;
    //   Optional/Show Ad Debug Overlay               — the GAMEPIX_AD_DEBUG define;
    //   Optional/Xsolla ID via JS Bridge (GAMEPIX_JS) — the GAMEPIX_JS define.
    //
    // The options themselves are described in GamePixOptions and the asmdef
    // file work lives in GamePixAsmdefConstraints. Only the menu items remain
    // here, and they have to: a MenuItem path is a compile-time constant and
    // each item needs its own method, so items cannot be generated from the
    // table. The Build Manager window is drawn from the same table, so it can
    // never disagree with the menu.
    //
    // Menu items cannot have tooltips (MenuItem only has menuItem / priority /
    // secondaryPriority / validate / editorModes), so the details live in the
    // option's tooltip, which the Build Manager window shows before a click.
    // They are also written to the log on toggle.
    //
    // Why this assembly is NOT constrained by defineConstraints and does not
    // reference LongLeap.GamePix: the script that turns GAMEPIX on cannot itself
    // require GAMEPIX, or it would vanish exactly when it is needed. For the
    // same reason it lives in the package rather than in Assets/Plugins/GamePix:
    // updating the plugin means deleting that folder entirely, and the menu
    // would go with it. Referencing LongLeap.BuildManager.Options does not break the
    // rule: a reference is not a constraint, and LongLeap.BuildManager.Options is not
    // constrained either.
    public static class GamePixPluginMenu
    {
        // Package HTML5 Zip stays at the root: it is an action, not a project
        // state toggle.
        private const string EnabledMenuPath = "GamePix/Required/Plugin Enabled (GAMEPIX)";
        private const string ConstraintsMenuPath = "GamePix/Required/Plugin Has defineConstraints";
        private const string AnalyticsMenuPath = "GamePix/Required/Allow External Analytics (GA4)";
        private const string AdDebugMenuPath = "GamePix/Optional/Show Ad Debug Overlay";
        private const string JsBridgeMenuPath = "GamePix/Optional/Xsolla ID via JS Bridge (GAMEPIX_JS)";

        // ---------- Toggle 1: the define ----------

        [MenuItem(EnabledMenuPath, false, 200)]
        private static void TogglePluginEnabled() => ProjectOptionMenu.Toggle(GamePixOptions.EnabledId);

        [MenuItem(EnabledMenuPath, true)]
        private static bool ValidateTogglePluginEnabled() =>
            ProjectOptionMenu.Validate(EnabledMenuPath, GamePixOptions.EnabledId);

        // ---------- Toggle 2: constraints in the plugin's asmdefs ----------

        [MenuItem(ConstraintsMenuPath, false, 202)]
        private static void RestoreConstraints() =>
            ProjectOptionMenu.Toggle(GamePixOptions.ConstraintsId);

        [MenuItem(ConstraintsMenuPath, true)]
        private static bool ValidateRestoreConstraints() =>
            ProjectOptionMenu.Validate(ConstraintsMenuPath, GamePixOptions.ConstraintsId);

        // ---------- Toggle 3: external analytics ----------

        [MenuItem(AnalyticsMenuPath, false, 203)]
        private static void ToggleAllowAnalytics() =>
            ProjectOptionMenu.Toggle(GamePixOptions.AnalyticsId);

        [MenuItem(AnalyticsMenuPath, true)]
        private static bool ValidateToggleAllowAnalytics() =>
            ProjectOptionMenu.Validate(AnalyticsMenuPath, GamePixOptions.AnalyticsId);

        // ---------- Toggle 4: on-screen ad log ----------

        [MenuItem(AdDebugMenuPath, false, 204)]
        private static void ToggleAdDebug() => ProjectOptionMenu.Toggle(GamePixOptions.AdDebugId);

        [MenuItem(AdDebugMenuPath, true)]
        private static bool ValidateToggleAdDebug() =>
            ProjectOptionMenu.Validate(AdDebugMenuPath, GamePixOptions.AdDebugId);

        // ---------- Toggle 5: Xsolla ID through the jslib bridge ----------

        [MenuItem(JsBridgeMenuPath, false, 205)]
        private static void ToggleJsBridge() => ProjectOptionMenu.Toggle(GamePixOptions.JsBridgeId);

        [MenuItem(JsBridgeMenuPath, true)]
        private static bool ValidateToggleJsBridge() =>
            ProjectOptionMenu.Validate(JsBridgeMenuPath, GamePixOptions.JsBridgeId);
    }
}
