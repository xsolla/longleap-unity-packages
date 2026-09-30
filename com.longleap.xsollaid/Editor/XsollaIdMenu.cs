#if UNITY_EDITOR
using LongLeap.Core.Editor;
using UnityEditor;
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
    //     LongLeap.XsollaId (её нет намеренно);
    //   Optional/Debug Panel — дефайн XSOLLA_ID_DEBUG, только на время
    //     отладки; в сборку на площадку попадать не должен.
    //
    // Два дефайна теперь описаны в XsollaIdOptions, и та же таблица рисует
    // строки в окне Build Manager — поэтому меню и окно не могут разойтись.
    // Сами атрибуты [MenuItem] остались здесь и остаться обязаны: путь у них —
    // константа времени компиляции, и метод нужен на каждый пункт.
    //
    // СРЕДА осталась написанной руками и в окне НЕ показывается: это выбор из
    // двух, у которого своя проверка заполненности, и трогать его в этом
    // заходе незачем. Подсказок к пунктам меню по-прежнему нет и быть не
    // может — у MenuItem только menuItem/priority/secondaryPriority/validate/
    // editorModes; теперь они есть в окне, где взяты из той же таблицы.
    //
    // У этой сборки НЕТ defineConstraints намеренно: скрипт, который включает
    // XSOLLA_ID, не может сам его требовать — иначе включить будет нечем.
    // Ссылка на LongLeap.Core.Editor этого не нарушает: ссылка — не
    // ограничение, и сама LongLeap.Core.Editor тоже ничем не ограничена.
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

        // Отладочная кнопка входа (F9/F10/F11) для проверки до появления UI.
        // В сборку на ревью попадать не должна.
        [MenuItem(DebugMenuPath, false, 101)]
        private static void ToggleDebug() => ProjectOptionMenu.Toggle(XsollaIdOptions.DebugId);

        [MenuItem(DebugMenuPath, true)]
        private static bool ToggleDebugValidate() =>
            ProjectOptionMenu.Validate(DebugMenuPath, XsollaIdOptions.DebugId);

        // Среда — не переключатель, а выбор из двух, поэтому два пункта с
        // галочкой, а не один чекбокс: так видно, куда именно ходит вход, и не
        // приходится догадываться, что значит снятая галочка.

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

        // ---- среда ----

        private static bool ValidateEnvironment(string menuPath, string enumName)
        {
            SerializedProperty environment = XsollaIdConfigAccess.LoadEnvironmentProperty(out _);

            Menu.SetChecked(
                menuPath,
                environment != null
                && environment.enumValueIndex == XsollaIdConfigAccess.IndexOf(environment, enumName));

            // Ассета нет — переключать нечего.
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

        // Пустой clientId или redirect uri делают IsValid, а за ним и
        // IsAvailable, ложными: кнопка входа просто гаснет, ничего не сообщая.
        // Лучше сказать об этом в момент переключения.
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
