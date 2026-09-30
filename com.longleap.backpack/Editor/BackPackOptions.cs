using System.Collections.Generic;
using LongLeap.Core.Editor;
using UnityEditor;

namespace BloodRedSnow.BackPack.Editor
{
    // Среда BackPack в окне Build Manager и в меню.
    //
    // Эта сборка НЕ ссылается на Darkslide.BackPack: к полю обращаемся через
    // SerializedObject по строковому имени, как это делает XsollaIdMenu. Так
    // описатель не зависит от рантайм-сборки, а перестановка членов
    // BackPackEnvironment не переключит сервер молча — имена членов читаются
    // из enumNames и сверяются по имени.
    //
    // Почему среда живёт в ассете, а не в дефайне, объяснено в самом
    // BackPackConfigSO: адрес сервера — это данные.
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
                        "Какой из трёх наборов настроек уедет в сборку: куда отправлять " +
                        "события ачивок и от имени какой игры.",
                        required: true,
                        order: 100,
                        choices: new[]
                        {
                            new ProjectOptionChoice(
                                LocalName,
                                "Local",
                                "Сервер, поднятый на машине разработчика. Из сборки на площадке " +
                                "недоступен — это адрес для проверки в редакторе и локальной сборки."),
                            new ProjectOptionChoice(
                                DevelopmentName,
                                "Development",
                                "Общий стенд, по которому проверяют сборку. Только https: наружу " +
                                "открыт лишь Cloudflare."),
                            new ProjectOptionChoice(
                                ProductionName,
                                "Production",
                                "Боевой сервер. Тот же домен, что у стенда, без -dev."),
                        },
                        onApplied: WarnIfIncomplete),
                },
                actions: new[]
                {
                    new ProjectOptionAction(
                        "Select Config Asset",
                        $"Выделить {ConfigAssetPath} в проекте.",
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

            // Портальная сборка живёт в чужом iframe, откуда 127.0.0.1 — это
            // машина ИГРОКА, а не разработчика. События просто не дойдут.
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

            if (config == null)
                yield break;

            string prefix = FieldPrefix(environment);
            string baseUrl = GetString(config, prefix + "._baseUrl");
            string gameId = GetString(config, prefix + "._gameId");

            // Пустой адрес — это штатный выключатель, так написано в
            // BackPackConfigSO. Поэтому info, а не ошибка.
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

        // Аналог XsollaIdMenu.WarnIfIncomplete: сказать в момент переключения,
        // а не когда ачивки молча перестанут сохраняться.
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

        public static void SelectConfigAsset()
        {
            var config = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ConfigAssetPath);

            if (config == null)
            {
                UnityEngine.Debug.LogError(
                    $"BackPack: config not found at {ConfigAssetPath}. " +
                    "Create it via Assets / Create / Blood Red Snow / BackPack Config.");
                return;
            }

            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);
        }
    }
}
