#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace LongLeap.XsollaId.Editor
{
    // Access to the config asset, shared by XsollaIdMenu and XsollaIdWarnings
    // so the serialized field names are written in one place only.
    //
    // Field names are strings and there is deliberately no typed reference to
    // XsollaIdConfigSO: this assembly must not reference LongLeap.XsollaId (see
    // the XsollaIdMenu header).
    internal static class XsollaIdConfigAccess
    {
        public const string ConfigAssetPath = "Assets/Resources/Backend/XsollaIdConfig.asset";

        public const string EnvironmentProperty = "_environment";
        public const string StageEnumName = "Stage";
        public const string ProductionEnumName = "Production";

        public const string AllowedGameOriginsProperty = "_allowedGameOrigins";

        public static SerializedProperty LoadEnvironmentProperty(out SerializedObject config)
        {
            var asset = AssetDatabase.LoadAssetAtPath<Object>(ConfigAssetPath);

            config = asset == null ? null : new SerializedObject(asset);

            return config?.FindProperty(EnvironmentProperty);
        }

        // Look the index up by name instead of hardcoding 0/1: reordering the
        // XsollaIdEnvironment members would otherwise silently switch the
        // environment.
        public static int IndexOf(SerializedProperty environment, string enumName) =>
            System.Array.IndexOf(environment.enumNames, enumName);

        // Name of the active environment, or null if there is no asset.
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

        // The allowedGameOrigins list as written in the asset, or null if there
        // is no asset.
        public static string[] LoadAllowedGameOrigins()
        {
            var asset = AssetDatabase.LoadAssetAtPath<Object>(ConfigAssetPath);

            if (asset == null)
                return null;

            SerializedProperty list = new SerializedObject(asset).FindProperty(AllowedGameOriginsProperty);

            if (list == null || !list.isArray)
                return System.Array.Empty<string>();

            var origins = new string[list.arraySize];

            for (int i = 0; i < origins.Length; i++)
                origins[i] = list.GetArrayElementAtIndex(i).stringValue;

            return origins;
        }

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
