#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace LongLeap.XsollaId.Editor
{
    // Доступ к ассету конфига. Раньше это жило внутри XsollaIdMenu, но теперь
    // к тем же полям обращаются ещё и предупреждения окна, а третьей копии
    // строковых имён полей проект уже не выдержит.
    //
    // Имена полей — строки, типизированной ссылки на XsollaIdConfigSO нет
    // намеренно: ссылки на LongLeap.XsollaId у этой сборки не должно быть,
    // иначе она рискует исчезнуть вместе с ней (см. заголовок XsollaIdMenu).
    internal static class XsollaIdConfigAccess
    {
        public const string ConfigAssetPath = "Assets/Resources/Backend/XsollaIdConfig.asset";

        public const string EnvironmentProperty = "_environment";
        public const string StageEnumName = "Stage";
        public const string ProductionEnumName = "Production";

        public static SerializedProperty LoadEnvironmentProperty(out SerializedObject config)
        {
            var asset = AssetDatabase.LoadAssetAtPath<Object>(ConfigAssetPath);

            config = asset == null ? null : new SerializedObject(asset);

            return config?.FindProperty(EnvironmentProperty);
        }

        // Индекс ищем по имени, а не пишем 0/1 числом: перестановка членов
        // XsollaIdEnvironment иначе тихо переключила бы среду.
        public static int IndexOf(SerializedProperty environment, string enumName) =>
            System.Array.IndexOf(environment.enumNames, enumName);

        // Имя активной среды или null, если ассета нет.
        public static string CurrentEnvironment()
        {
            SerializedProperty environment = LoadEnvironmentProperty(out _);

            if (environment == null)
                return null;

            string[] names = environment.enumNames;
            int index = environment.enumValueIndex;

            return index >= 0 && index < names.Length ? names[index] : null;
        }

        public static string FieldPrefix(string enumName) =>
            enumName == ProductionEnumName ? "_production" : "_stage";

        public static string GetString(SerializedObject config, string field) =>
            config?.FindProperty(field)?.stringValue;

        public static void SelectConfigAsset()
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
    }
}
#endif
