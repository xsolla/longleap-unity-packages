using System.Collections.Generic;
using System.IO;
using System.Linq;
using LongLeap.Core.Editor;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LongLeap.BuildManager.Editor
{
    // The plain WebGL build: the default way to build.
    //
    // Always applicable and has the lowest priority, so any more specific way
    // (a portal's vendor builder, for example) overrides it.
    internal sealed class WebGLPlayerBuildTarget : IProjectBuildTargetProvider, IProjectWarningProvider
    {
        public const string TargetId = "webgl.player";

        // The path belongs to the WAY of building, not to the preset: another
        // build target picks its own output folder anyway, so this is not the
        // preset's decision to make. Set in Project Settings / LongLeap /
        // Build Manager.
        private static string OutputPath => BuildManagerSettings.instance.WebGLOutputPath;

        private const string CallbackPage = "xid-callback.html";

        public IEnumerable<ProjectBuildTarget> GetBuildTargets()
        {
            yield return new ProjectBuildTarget(
                TargetId,
                $"WebGL -> {OutputPath}",
                0,
                Build,
                description: $"BuildPipeline.BuildPlayer into {OutputPath}.");
        }

        public IEnumerable<ProjectWarning> GetWarnings(IProjectOptionState pendingState)
        {
            // Warn ONLY if this way is the one that will build. The Addressables
            // prepass runs only here, and another build target may set its own
            // compression (a portal package, for example, may compress itself
            // and need Disabled); demanding Brotli there would cry wolf on
            // every such build.
            if (ProjectBuildTargets.Select(pendingState)?.Id != TargetId)
                yield break;

            string outputProblem = BuildManagerSettings.ValidateOutputPath(OutputPath);

            if (outputProblem != null)
            {
                yield return new ProjectWarning(
                    ProjectWarningSeverity.Error,
                    outputProblem,
                    blocksBuild: true,
                    fixLabel: "Open Build Manager Settings",
                    fix: () => SettingsService.OpenProjectSettings(BuildManagerSettings.SettingsPath));
            }

            switch (AddressablesPrepass.Check(out string addressablesProblem))
            {
                case AddressablesPrepass.Outcome.WillBuild:
                    yield return new ProjectWarning(
                        ProjectWarningSeverity.Info,
                        "Addressables is set not to build content with the player, so this window " +
                        "will build the content bundles before the player. Without that the build " +
                        "would ship whatever bundles were left from last time.");
                    break;

                case AddressablesPrepass.Outcome.Failed:
                    // Run would cancel the player build for the same reason, so
                    // say it before Build is pressed, not after.
                    yield return new ProjectWarning(
                        ProjectWarningSeverity.Error,
                        addressablesProblem,
                        blocksBuild: true);
                    break;
            }

            // Info, not Warning: the right compression depends on the host, and
            // a host that sends Content-Encoding headers is fine without the
            // fallback. This is a recommendation for plain static hosting.
            WebGLCompressionFormat compression = PlayerSettings.WebGL.compressionFormat;

            if (compression != WebGLCompressionFormat.Brotli)
            {
                yield return new ProjectWarning(
                    ProjectWarningSeverity.Info,
                    $"WebGL Compression Format is {compression}. A self-hosted HTML5 upload wants " +
                    "Brotli with Decompression Fallback on — that is what serves smallest over a " +
                    "plain static host.",
                    fixLabel: "Open Player Settings",
                    fix: () => SettingsService.OpenProjectSettings("Project/Player"));
            }
            else if (!PlayerSettings.WebGL.decompressionFallback)
            {
                yield return new ProjectWarning(
                    ProjectWarningSeverity.Info,
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

            string outputProblem = BuildManagerSettings.ValidateOutputPath(OutputPath);

            if (outputProblem != null)
            {
                Debug.LogError($"Build Manager: {outputProblem}");
                return;
            }

            AddressablesPrepass.Outcome outcome = AddressablesPrepass.Run(out string message);

            switch (outcome)
            {
                case AddressablesPrepass.Outcome.Failed:
                    // Stop here: building the player with stale bundles is
                    // worse than not building at all.
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

                // For WebGL this is a FOLDER, with no extension.
                locationPathName = OutputPath,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,

                // Deliberately not AutoRunPlayer: Unity would open the build on
                // localhost, but sign-in needs exactly 127.0.0.1. That is a
                // different origin, the redirect uri is registered for it, and
                // the IndexedDB holding the saves is separate too. Run serve.py
                // instead.
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
                $"Build Manager: WebGL build succeeded in " +
                $"{(int)summary.totalTime.TotalMinutes}m {summary.totalTime.Seconds}s, " +
                $"{summary.totalSize / (1024.0 * 1024.0):F1} MB at {OutputPath}. " +
                "Serve it with \"python serve.py\" and open http://127.0.0.1:8081 " +
                "(127.0.0.1 exactly — localhost is a different origin).");

            WarnIfCallbackPageMissing();
        }

        // The same check serve.py makes: with sign-in on, the callback page must
        // sit next to index.html. The XsollaIdCallbackPagePostprocessor
        // post-process step puts it there, and if it is missing, sign-in in this
        // build is dead even though the build "succeeded".
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
