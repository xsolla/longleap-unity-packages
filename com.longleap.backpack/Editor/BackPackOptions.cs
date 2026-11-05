using System.Collections.Generic;
using LongLeap.BuildManager.Options;
using UnityEditor;

namespace LongLeap.Backpack.Editor
{
    // The BackPack environment in the Build Manager window and in the menu.
    //
    // This assembly does NOT reference LongLeap.Backpack: the field is accessed
    // through SerializedObject by its string name, as XsollaIdMenu does. This way
    // the descriptor does not depend on the runtime assembly, and reordering the
    // BackPackEnvironment members does not silently switch the server — member
    // names are read from enumNames and matched by name.
    //
    // Why the environment lives in the asset rather than in a define is explained
    // in BackPackConfigSO itself: the server address is data.
    internal sealed class BackPackOptions : IProjectOptionSectionProvider, IProjectWarningProvider
    {
        public const string EnvironmentId = "backpack.environment";

        public const string ConfigAssetPath = "Assets/Resources/Backend/BackPackConfig.asset";

        public const string EnvironmentProperty = "_environment";

        public const string LocalName = "Local";
        public const string DevelopmentName = "Development";
        public const string ProductionName = "Production";

        public IEnumerable<ProjectOptionSection> GetSections()
        {
            yield return new ProjectOptionSection(
                "backpack",
                "BackPack",
                30,
                new ProjectOption[]
                {
                    new AssetEnumOption(
                        EnvironmentId,
                        ConfigAssetPath,
                        EnvironmentProperty,
                        "Environment",
                        "Which of the three settings sets goes into the build: where achievement " +
                        "events are sent and on behalf of which game.",
                        required: true,
                        order: 100,
                        choices: new[]
                        {
                            new ProjectOptionChoice(
                                LocalName,
                                "Local",
                                "A server running on the developer's machine. Unreachable from a build " +
                                "on a platform — this address is for testing in the Editor and local builds."),
                            new ProjectOptionChoice(
                                DevelopmentName,
                                "Development",
                                "The shared staging server builds are tested against. https only: only " +
                                "Cloudflare is exposed."),
                            new ProjectOptionChoice(
                                ProductionName,
                                "Production",
                                "The production server. Same domain as staging, without -dev."),
                        },
                        onApplied: WarnIfIncomplete),
                },
                actions: new[]
                {
                    new ProjectOptionAction(
                        "Select Config Asset",
                        $"Select {ConfigAssetPath} in the project; if it is missing, offer to create it there.",
                        100,
                        SelectConfigAsset),
                });
        }

        public IEnumerable<ProjectWarning> GetWarnings(IProjectOptionState pendingState)
        {
            if (pendingState == null)
                yield break;

            string environment = pendingState.GetValue(EnvironmentId);

            if (environment == null)
                yield break;

            // A portal build lives in someone else's iframe, where 127.0.0.1 is the
            // PLAYER's machine, not the developer's. Events simply will not arrive.
            if (environment == LocalName && pendingState.IsDefineOn("GAMEPIX"))
            {
                yield return new ProjectWarning(
                    ProjectWarningSeverity.Warning,
                    "BackPack is set to Local while GAMEPIX is on. A portal build runs in an " +
                    "iframe on the platform, where 127.0.0.1 is the player's own machine — " +
                    "achievement events will not arrive.",
                    fixLabel: "Select Config Asset",
                    fix: SelectConfigAsset);
            }

            var config = LoadConfig();

            // Without the asset BackPackTask registers NullBackPackService: the game
            // works, but no events are sent and there is no backpack. Not a build
            // error, but not something to learn from a running game's log either.
            if (config == null)
            {
                yield return new ProjectWarning(
                    ProjectWarningSeverity.Warning,
                    $"BackPack config not found at {ConfigAssetPath}, so BackPack is off in this " +
                    "build: no events are sent and the backpack window shows an error.",
                    fixLabel: "Create Config Asset",
                    fix: SelectConfigAsset);
                yield break;
            }

            string prefix = FieldPrefix(environment);
            string baseUrl = GetString(config, prefix + "._baseUrl");
            string gameId = GetString(config, prefix + "._gameId");

            // An empty address is the regular off switch, as BackPackConfigSO says.
            // Hence info rather than an error.
            if (string.IsNullOrEmpty(baseUrl?.Trim()))
            {
                yield return new ProjectWarning(
                    ProjectWarningSeverity.Info,
                    $"BackPack environment {environment} has an empty baseUrl, so events are not " +
                    "sent at all and achievements stay local. That is a valid switch — mentioned " +
                    "in case it was not deliberate.");
            }
            else if (string.IsNullOrEmpty(gameId?.Trim()))
            {
                yield return new ProjectWarning(
                    ProjectWarningSeverity.Error,
                    $"BackPack environment {environment} has a baseUrl but no gameId. The server " +
                    "will reject every event, and achievements will stop being saved.",
                    fixLabel: "Select Config Asset",
                    fix: SelectConfigAsset);
            }
        }

        // Counterpart of XsollaIdMenu.WarnIfIncomplete: speak up at the moment of
        // switching, not when achievements silently stop being saved.
        private static void WarnIfIncomplete(string environment, IProjectWriteContext context)
        {
            SerializedObject config = context.GetSerializedObject(ConfigAssetPath);

            if (config == null)
                return;

            string prefix = FieldPrefix(environment);
            string baseUrl = GetString(config, prefix + "._baseUrl");
            string gameId = GetString(config, prefix + "._gameId");

            if (string.IsNullOrEmpty(baseUrl?.Trim()))
            {
                context.Log(
                    $"BackPack: environment is now {environment}, and its baseUrl is empty — " +
                    "events are not sent, achievements stay local.");
                return;
            }

            if (string.IsNullOrEmpty(gameId?.Trim()))
            {
                context.LogError(
                    $"BackPack: environment is now {environment}, but its gameId is empty — the " +
                    $"server will reject every event. Fill it in {ConfigAssetPath}.");
                return;
            }

            context.Log($"BackPack: environment is now {environment} ({baseUrl.Trim()}).");
        }

        public static string FieldPrefix(string environment)
        {
            switch (environment)
            {
                case ProductionName: return "_production";
                case DevelopmentName: return "_development";
                default: return "_local";
            }
        }

        private static SerializedObject LoadConfig()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ConfigAssetPath);

            return asset == null ? null : new SerializedObject(asset);
        }

        private static string GetString(SerializedObject config, string path) =>
            config?.FindProperty(path)?.stringValue;

        // Select the config, or offer to create it when it is missing.
        //
        // Created here and at exactly ConfigAssetPath: Assets / Create puts the
        // asset into the selected folder, while the runtime looks for it only at
        // Resources/Backend/BackPackConfig. A config created "elsewhere" would
        // simply go unseen by the game — silently.
        public static void SelectConfigAsset()
        {
            var config = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ConfigAssetPath);

            if (config == null)
            {
                if (!EditorUtility.DisplayDialog(
                        "BackPack config not found",
                        $"There is no BackPack config at\n{ConfigAssetPath}\n\n" +
                        "Create it there with the default endpoints?",
                        "Create",
                        "Cancel"))
                {
                    UnityEngine.Debug.LogError(
                        $"BackPack: config not found at {ConfigAssetPath}. Create it with BackPack / " +
                        "Select Config Asset, or via Assets / Create / LongLeap Services / BackPack Config " +
                        $"and move it to exactly {ConfigAssetPath} — the game loads it only from there.");
                    return;
                }

                config = CreateConfigAsset();

                if (config == null)
                    return;
            }

            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);
        }

        // The type is looked up by name rather than through a LongLeap.Backpack
        // reference — for the same reason the fields are read via SerializedObject
        // (see above).
        private const string ConfigTypeName = "LongLeap.Backpack.BackPackConfigSO, LongLeap.Backpack";

        private static UnityEngine.Object CreateConfigAsset()
        {
            var type = System.Type.GetType(ConfigTypeName);

            if (type == null)
            {
                UnityEngine.Debug.LogError(
                    $"BackPack: type {ConfigTypeName} not found — is the LongLeap.Backpack assembly compiled?");
                return null;
            }

            EnsureFolder(System.IO.Path.GetDirectoryName(ConfigAssetPath)?.Replace('\\', '/'));

            var asset = UnityEngine.ScriptableObject.CreateInstance(type);
            AssetDatabase.CreateAsset(asset, ConfigAssetPath);
            AssetDatabase.SaveAssets();

            UnityEngine.Debug.Log(
                $"BackPack: created {ConfigAssetPath} with default endpoints. Check the game ID and " +
                "select the environment (BackPack / Required / Environment).", asset);

            return asset;
        }

        // AssetDatabase.CreateFolder creates one level at a time — walk the whole path.
        private static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
                return;

            int slash = folder.LastIndexOf('/');
            string parent = folder.Substring(0, slash);

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder.Substring(slash + 1));
        }
    }
}
