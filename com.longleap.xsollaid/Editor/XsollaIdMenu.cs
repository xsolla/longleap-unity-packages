#if UNITY_EDITOR
using LongLeap.BuildManager.Options;
using UnityEditor;
using UnityEngine;

namespace LongLeap.XsollaId.Editor
{
    // Xsolla ID sign-in toggle and quick access to the config asset.
    //
    // The menu is split into submenus, following the same convention as the
    // GamePix package's menu:
    //
    //   Required/Sign-in Enabled (XSOLLA_ID) — the define without which the
    //     LongLeap.XsollaId.Web assembly does not exist and sign-in is not built;
    //   Required/Environment/{Stage,Production} — which client registration is
    //     active. This is not a define but a field in the config asset; it is
    //     edited through SerializedObject so this assembly does not need a
    //     reference to LongLeap.XsollaId (it deliberately has none);
    //   Optional/Debug Panel — the XSOLLA_ID_DEBUG define, for debugging only;
    //     it must not reach a portal build.
    //
    // Both defines are described in XsollaIdOptions, and the same table draws
    // the rows in the Build Manager window, so the menu and the window cannot
    // drift apart. The [MenuItem] attributes have to stay here: their path is a
    // compile-time constant and each item needs its own method.
    //
    // The environment is handled by hand and is NOT shown in the window: it is
    // a choice between two values with its own completeness check. Menu items
    // cannot have tooltips (MenuItem only has menuItem/priority/
    // secondaryPriority/validate/editorModes); the window shows them instead,
    // taken from the same table.
    //
    // This assembly deliberately has NO defineConstraints: the script that
    // turns XSOLLA_ID on cannot itself require it, or there would be nothing
    // left to turn it on with. The reference to LongLeap.BuildManager.Options does not
    // break this: a reference is not a constraint, and LongLeap.BuildManager.Options is
    // unconstrained as well.
    public static class XsollaIdMenu
    {
        private const string EnabledMenuPath = "Xsolla ID/Required/Sign-in Enabled (XSOLLA_ID)";
        private const string DebugMenuPath = "Xsolla ID/Optional/Debug Panel (on-screen)";
        private const string ConfigMenuPath = "Xsolla ID/Select Config Asset";

        private const string EnvStageMenuPath = "Xsolla ID/Required/Environment/Stage";
        private const string EnvProductionMenuPath = "Xsolla ID/Required/Environment/Production";

        [MenuItem(EnabledMenuPath, false, 100)]
        private static void ToggleEnabled() => ProjectOptionMenu.Toggle(XsollaIdOptions.EnabledId);

        [MenuItem(EnabledMenuPath, true)]
        private static bool ToggleEnabledValidate() =>
            ProjectOptionMenu.Validate(EnabledMenuPath, XsollaIdOptions.EnabledId);

        // Debug sign-in trigger (F9/F10/F11) for testing without the UI.
        // Must not reach a build submitted for review.
        [MenuItem(DebugMenuPath, false, 101)]
        private static void ToggleDebug() => ProjectOptionMenu.Toggle(XsollaIdOptions.DebugId);

        [MenuItem(DebugMenuPath, true)]
        private static bool ToggleDebugValidate() =>
            ProjectOptionMenu.Validate(DebugMenuPath, XsollaIdOptions.DebugId);

        // The environment is a choice between two values, not a toggle, so it
        // is two checkable items rather than one checkbox: it shows exactly
        // where sign-in goes, with no guessing what an unchecked box means.

        [MenuItem(EnvStageMenuPath, false, 103)]
        private static void SelectStage() => SetEnvironment(XsollaIdConfigAccess.StageEnumName);

        [MenuItem(EnvStageMenuPath, true)]
        private static bool SelectStageValidate() =>
            ValidateEnvironment(EnvStageMenuPath, XsollaIdConfigAccess.StageEnumName);

        [MenuItem(EnvProductionMenuPath, false, 104)]
        private static void SelectProduction() =>
            SetEnvironment(XsollaIdConfigAccess.ProductionEnumName);

        [MenuItem(EnvProductionMenuPath, true)]
        private static bool SelectProductionValidate() =>
            ValidateEnvironment(EnvProductionMenuPath, XsollaIdConfigAccess.ProductionEnumName);

        [MenuItem(ConfigMenuPath, false, 102)]
        private static void SelectConfig() => XsollaIdConfigAccess.SelectConfigAsset();

        // ---- environment ----

        private static bool ValidateEnvironment(string menuPath, string enumName)
        {
            SerializedProperty environment = XsollaIdConfigAccess.LoadEnvironmentProperty(out _);

            Menu.SetChecked(
                menuPath,
                environment != null
                && environment.enumValueIndex == XsollaIdConfigAccess.IndexOf(environment, enumName));

            // No asset — nothing to switch.
            return environment != null;
        }

        private static void SetEnvironment(string enumName)
        {
            SerializedProperty environment =
                XsollaIdConfigAccess.LoadEnvironmentProperty(out SerializedObject config);

            if (environment == null)
            {
                Debug.LogError(
                    $"Xsolla ID: config not found at {XsollaIdConfigAccess.ConfigAssetPath} " +
                    $"(or it has no {XsollaIdConfigAccess.EnvironmentProperty} field).");
                return;
            }

            int index = XsollaIdConfigAccess.IndexOf(environment, enumName);

            if (index < 0)
            {
                Debug.LogError($"Xsolla ID: XsollaIdEnvironment has no member '{enumName}' — the enum was renamed.");
                return;
            }

            if (environment.enumValueIndex != index)
            {
                environment.enumValueIndex = index;
                config.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
            }

            Debug.Log($"Xsolla ID: environment is now {enumName}.");

            WarnIfIncomplete(config, enumName);
        }

        // An empty clientId or redirect uri makes IsValid, and therefore
        // IsAvailable, false: the sign-in button just goes away without a word.
        // Better to say so at the moment of switching.
        private static void WarnIfIncomplete(SerializedObject config, string enumName)
        {
            string prefix = XsollaIdConfigAccess.FieldPrefix(enumName);

            foreach (string field in new[] { "ClientId", "RedirectUri" })
            {
                SerializedProperty property = config.FindProperty(prefix + field);

                if (property == null || !string.IsNullOrEmpty(property.stringValue))
                    continue;

                Debug.LogError(
                    $"Xsolla ID: {prefix}{field} is empty — sign-in will report Unavailable and the button " +
                    $"will stay disabled. Fill it in {XsollaIdConfigAccess.ConfigAssetPath}.");
            }
        }
    }
}
#endif
