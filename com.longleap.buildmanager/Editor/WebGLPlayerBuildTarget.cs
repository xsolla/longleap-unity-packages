using System.Collections.Generic;
using System.IO;
using System.Linq;
using LongLeap.Core.Editor;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LongLeap.BuildManager.Editor
{
    // Обычная WebGL-сборка: то, чем собирают всё, кроме портальной версии.
    //
    // Подходит всегда и имеет наименьший приоритет, поэтому любой более
    // конкретный способ (вендорный билдер площадки) её перебивает.
    //
    // Этот класс закрывает дыру: в проекте НЕ БЫЛО точки входа в сборку вовсе.
    // На «Tools / Build WebGL» ссылаются serve.py и GamePixHtml5Packager, но
    // самого BuildScript.cs в репозитории давно нет.
    internal sealed class WebGLPlayerBuildTarget : IProjectBuildTargetProvider, IProjectWarningProvider
    {
        // Путь принадлежит СПОСОБУ сборки, а не пресету: вендорный билдер
        // площадки всё равно подменяет его на свой (html5), так что пресету
        // такое решение не принадлежит.
        //
        // Именно этот путь serve.py проверяет первым:
        // CANDIDATE_DIRS = ('Builds/WebGL', 'html5').
        public const string OutputPath = "Builds/WebGL";

        private const string CallbackPage = "xid-callback.html";

        public IEnumerable<ProjectBuildTarget> GetBuildTargets()
        {
            yield return new ProjectBuildTarget(
                "webgl.player",
                $"WebGL -> {OutputPath}",
                0,
                Build,
                description: $"BuildPipeline.BuildPlayer into {OutputPath}.");
        }

        public IEnumerable<ProjectWarning> GetWarnings(IProjectOptionState pendingState)
        {
            if (AddressablesPrepass.IsInstalled && !AddressablesPrepass.WillUnityBuildContent())
            {
                yield return new ProjectWarning(
                    ProjectWarningSeverity.Info,
                    "Addressables is set not to build content with the player, so this window will " +
                    "build the content bundles before the player. Without that the build would ship " +
                    "whatever bundles were left from last time.");
            }

            // Про сжатие предупреждаем ТОЛЬКО если собирать будет этот способ.
            // Вендорный билдер площадки всё равно ставит Disabled сам — .gpx
            // сжимает себя, — и требовать там Brotli значило бы кричать
            // «волки» на каждой портальной сборке.
            if (ProjectBuildTargets.Select(pendingState)?.Id != "webgl.player")
                yield break;

            WebGLCompressionFormat compression = PlayerSettings.WebGL.compressionFormat;

            if (compression != WebGLCompressionFormat.Brotli)
            {
                yield return new ProjectWarning(
                    ProjectWarningSeverity.Warning,
                    $"WebGL Compression Format is {compression}. A self-hosted HTML5 upload wants " +
                    "Brotli with Decompression Fallback on — that is what serves smallest over a " +
                    "plain static host.",
                    fixLabel: "Open Player Settings",
                    fix: () => SettingsService.OpenProjectSettings("Project/Player"));
            }
            else if (!PlayerSettings.WebGL.decompressionFallback)
            {
                yield return new ProjectWarning(
                    ProjectWarningSeverity.Warning,
                    "WebGL compression is Brotli but Decompression Fallback is off. A static host " +
                    "that does not send the right Content-Encoding will then fail to load the build.",
                    fixLabel: "Open Player Settings",
                    fix: () => SettingsService.OpenProjectSettings("Project/Player"));
            }
        }

        private static void Build()
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Debug.LogError("Build Manager: no scene is enabled in the build settings.");
                return;
            }

            AddressablesPrepass.Outcome outcome = AddressablesPrepass.Run(out string message);

            switch (outcome)
            {
                case AddressablesPrepass.Outcome.Failed:
                    // Останавливаемся: собрать плеер со старыми бандлами хуже,
                    // чем не собрать вовсе.
                    Debug.LogError($"Build Manager: {message} The player build was cancelled.");
                    return;

                case AddressablesPrepass.Outcome.Built:
                    Debug.Log("Build Manager: Addressables content rebuilt.");
                    break;

                case AddressablesPrepass.Outcome.NoSettings:
                    Debug.Log("Build Manager: Addressables is installed but has no settings — skipped.");
                    break;

                case AddressablesPrepass.Outcome.AlreadyBuiltWithPlayer:
                    Debug.Log("Build Manager: Unity builds Addressables content with the player — skipped.");
                    break;
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes,

                // Для WebGL это ПАПКА, без расширения.
                locationPathName = OutputPath,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,

                // Сознательно не AutoRunPlayer: Unity открыла бы сборку на
                // localhost, а вход требует ровно 127.0.0.1 — это другой
                // origin, под него зарегистрирован redirect uri, и IndexedDB
                // с сохранениями тоже отдельный. Запускать надо serve.py.
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);

            if (report == null)
            {
                Debug.LogError("Build Manager: the build produced no report.");
                return;
            }

            BuildSummary summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError(
                    $"Build Manager: WebGL build {summary.result} " +
                    $"({summary.totalErrors} error(s)). Output: {OutputPath}");
                return;
            }

            Debug.Log(
                $"Build Manager: WebGL build succeeded in {summary.totalTime}, " +
                $"{summary.totalSize / (1024 * 1024)} MB at {OutputPath}. " +
                "Serve it with \"python serve.py\" and open http://127.0.0.1:8081 " +
                "(127.0.0.1 exactly — localhost is a different origin).");

            WarnIfCallbackPageMissing();
        }

        // То же, что проверяет serve.py: при включённом входе страница возврата
        // обязана лежать рядом с index.html. Её кладёт постпроцесс
        // XsollaIdCallbackPagePostprocessor, и если её нет — вход в этой
        // сборке мёртв, хотя сборка «удалась».
        private static void WarnIfCallbackPageMissing()
        {
            if (!ScriptingDefines.IsOn("XSOLLA_ID"))
                return;

            if (File.Exists(Path.Combine(OutputPath, CallbackPage)))
                return;

            Debug.LogError(
                $"Build Manager: XSOLLA_ID is on but {OutputPath}/{CallbackPage} is missing. " +
                "Sign-in will fail in this build — the callback page post-processor did not run.");
        }
    }
}
