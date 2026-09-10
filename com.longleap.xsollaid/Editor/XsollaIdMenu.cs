#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace LongLeap.XsollaId.Editor
{
    // Переключатель входа через Xsolla ID и быстрый доступ к конфигу.
    //
    // Меню разбито на подменю по тому же правилу, что и GamePixPluginMenu:
    //
    //   Required/Sign-in Enabled (XSOLLA_ID) — дефайн, без которого сборок
    //     LongLeap.XsollaId.Web в проекте нет вовсе и вход не соберётся;
    //   Required/Environment/{Stage,Production} — какая регистрация активна.
    //     Это не дефайн, а поле в ассете конфига; правим его через
    //     SerializedObject, чтобы у сборки не появилось ссылки на
    //     LongLeap.XsollaId (её нет намеренно, см. SelectConfig);
    //   Optional/Debug Panel — дефайн XSOLLA_ID_DEBUG, только на время
    //     отладки; в сборку на площадку попадать не должен.
    //
    // Select Config Asset остаётся в корне: это действие (выделить ассет), а
    // не переключатель состояния проекта — ровно как Package HTML5 Zip у
    // GamePix.
    //
    // Подсказок к пунктам нет и быть не может: у атрибута MenuItem только
    // menuItem/priority/secondaryPriority/validate/editorModes. Подробности —
    // в Debug.Log при переключении.
    //
    // У этой сборки НЕТ defineConstraints намеренно: скрипт, который включает
    // XSOLLA_ID, не может сам его требовать — иначе включить будет нечем. Тот
    // же приём и та же причина, что у GamePixPluginMenu.
    public static class XsollaIdMenu
    {
        private const string Define = "XSOLLA_ID";
        private const string DebugDefine = "XSOLLA_ID_DEBUG";
        private const string EnabledMenuPath = "Xsolla ID/Required/Sign-in Enabled (XSOLLA_ID)";
        private const string DebugMenuPath = "Xsolla ID/Optional/Debug Panel (on-screen)";
        private const string ConfigMenuPath = "Xsolla ID/Select Config Asset";
        private const string ConfigAssetPath = "Assets/Resources/Backend/XsollaIdConfig.asset";

        private const string EnvStageMenuPath = "Xsolla ID/Required/Environment/Stage";
        private const string EnvProductionMenuPath = "Xsolla ID/Required/Environment/Production";

        // Имена полей ассета. Совпадают с полями XsollaIdConfigSO — типизированно
        // сослаться на него нельзя, ссылки на LongLeap.XsollaId у этой сборки нет.
        private const string EnvironmentProperty = "_environment";
        private const string StageEnumName = "Stage";
        private const string ProductionEnumName = "Production";

        [MenuItem(EnabledMenuPath, false, 100)]
        private static void ToggleEnabled()
        {
            bool enabled = IsEnabled();

            SetDefine(!enabled);

            Debug.Log(enabled
                ? "Xsolla ID: sign-in disabled (XSOLLA_ID removed). The build will compile with no network calls to id.xsolla.com."
                : "Xsolla ID: sign-in enabled (XSOLLA_ID added). Do not forget to fill in clientId in " + ConfigAssetPath);
        }

        [MenuItem(EnabledMenuPath, true)]
        private static bool ToggleEnabledValidate()
        {
            Menu.SetChecked(EnabledMenuPath, IsEnabled());
            return true;
        }

        // Среда — не переключатель, а выбор из двух, поэтому два пункта с
        // галочкой, а не один чекбокс: так видно, куда именно ходит вход, и не
        // приходится догадываться, что значит снятая галочка.

        [MenuItem(EnvStageMenuPath, false, 103)]
        private static void SelectStage() => SetEnvironment(StageEnumName);

        [MenuItem(EnvStageMenuPath, true)]
        private static bool SelectStageValidate() => ValidateEnvironment(EnvStageMenuPath, StageEnumName);

        [MenuItem(EnvProductionMenuPath, false, 104)]
        private static void SelectProduction() => SetEnvironment(ProductionEnumName);

        [MenuItem(EnvProductionMenuPath, true)]
        private static bool SelectProductionValidate() =>
            ValidateEnvironment(EnvProductionMenuPath, ProductionEnumName);

        // Отладочная кнопка входа (F9/F10/F11) для проверки до появления UI.
        // В сборку на ревью попадать не должна.
        [MenuItem(DebugMenuPath, false, 101)]
        private static void ToggleDebug()
        {
            bool enabled = IsDebugEnabled();

            SetDefine(DebugDefine, !enabled);

            Debug.Log(enabled
                ? "Xsolla ID: the debug sign-in trigger is off."
                : "Xsolla ID: the debug trigger is on — F9 sign in, F10 sign out, F11 state. " +
                  "Works only in a WebGL build; turn it off before submitting to the portal.");
        }

        [MenuItem(DebugMenuPath, true)]
        private static bool ToggleDebugValidate()
        {
            Menu.SetChecked(DebugMenuPath, IsDebugEnabled());

            // Без самого входа отлаживать нечего.
            return IsEnabled();
        }

        [MenuItem(ConfigMenuPath, false, 102)]
        private static void SelectConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<Object>(ConfigAssetPath);

            if (config == null)
            {
                Debug.LogError(
                    $"Xsolla ID: config not found at {ConfigAssetPath}. " +
                    "Create it via Assets / Create / LongLeap Services / Xsolla ID Config.");
                return;
            }

            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);
        }

        // ---- среда ----

        private static SerializedProperty LoadEnvironmentProperty(out SerializedObject config)
        {
            var asset = AssetDatabase.LoadAssetAtPath<Object>(ConfigAssetPath);

            config = asset == null ? null : new SerializedObject(asset);

            return config?.FindProperty(EnvironmentProperty);
        }

        // Индекс ищем по имени, а не пишем 0/1 числом: перестановка членов
        // XsollaIdEnvironment иначе тихо переключила бы среду.
        private static int IndexOf(SerializedProperty environment, string enumName) =>
            System.Array.IndexOf(environment.enumNames, enumName);

        private static bool ValidateEnvironment(string menuPath, string enumName)
        {
            SerializedProperty environment = LoadEnvironmentProperty(out _);

            Menu.SetChecked(menuPath, environment != null && environment.enumValueIndex == IndexOf(environment, enumName));

            // Ассета нет — переключать нечего.
            return environment != null;
        }

        private static void SetEnvironment(string enumName)
        {
            SerializedProperty environment = LoadEnvironmentProperty(out SerializedObject config);

            if (environment == null)
            {
                Debug.LogError(
                    $"Xsolla ID: config not found at {ConfigAssetPath} (or it has no {EnvironmentProperty} field).");
                return;
            }

            int index = IndexOf(environment, enumName);

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

        // Пустой clientId или redirect uri делают IsValid, а за ним и
        // IsAvailable, ложными: кнопка входа просто гаснет, ничего не сообщая.
        // Лучше сказать об этом в момент переключения.
        private static void WarnIfIncomplete(SerializedObject config, string enumName)
        {
            string prefix = enumName == ProductionEnumName ? "_production" : "_stage";

            foreach (string field in new[] { "ClientId", "RedirectUri" })
            {
                SerializedProperty property = config.FindProperty(prefix + field);

                if (property == null || !string.IsNullOrEmpty(property.stringValue))
                    continue;

                Debug.LogError(
                    $"Xsolla ID: {prefix}{field} is empty — sign-in will report Unavailable and the button " +
                    $"will stay disabled. Fill it in {ConfigAssetPath}.");
            }
        }

        // ---- дефайны ----

        private static bool IsEnabled() => CurrentDefines().Contains(Define);

        private static bool IsDebugEnabled() => CurrentDefines().Contains(DebugDefine);

        private static List<string> CurrentDefines()
        {
            string raw = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.WebGL) ?? string.Empty;

            return raw.Split(';')
                .Select(d => d.Trim())
                .Where(d => d.Length > 0)
                .ToList();
        }

        private static void SetDefine(bool enabled) => SetDefine(Define, enabled);

        private static void SetDefine(string define, bool enabled)
        {
            List<string> defines = CurrentDefines();

            if (enabled)
            {
                if (!defines.Contains(define))
                    defines.Add(define);
            }
            else
            {
                defines.Remove(define);

                // Отлаживать нечего, если самого входа нет.
                if (define == Define)
                    defines.Remove(DebugDefine);
            }

            PlayerSettings.SetScriptingDefineSymbols(
                NamedBuildTarget.WebGL, string.Join(";", defines));
        }
    }
}
#endif
