using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;

namespace LongLeap.Backpack.Editor
{
    internal enum BackPackConfigProblem
    {
        None,
        Missing,
        Misplaced,
        Duplicates
    }

    // Finds every BackPack config in the project, wherever it lies.
    //
    // There must be exactly one, at BackPackOptions.ConfigAssetPath: the runtime
    // loads only Resources/Backend/BackPackConfig, and the menu and the Build
    // Manager edit only that path. A config created via Assets / Create lands in
    // the selected folder, and a copy (Ctrl+D, a merge, an old folder) is easy to
    // leave behind — both look configured in the inspector while the game reads
    // another asset or none at all. Hence the search over the whole project
    // rather than a look at the one path.
    internal sealed class BackPackConfigScan
    {
        // By name, without a LongLeap.Backpack reference — see BackPackOptions.
        private const string ConfigTypeName = "BackPackConfigSO";
        private const string ConfigTypeFullName = "LongLeap.Backpack." + ConfigTypeName;

        public IReadOnlyList<string> Paths { get; }

        public bool HasExpected { get; }

        public BackPackConfigProblem Problem { get; }

        private BackPackConfigScan(List<string> paths)
        {
            Paths = paths;
            HasExpected = paths.Contains(BackPackOptions.ConfigAssetPath);

            if (paths.Count == 0)
                Problem = BackPackConfigProblem.Missing;
            else if (paths.Count > 1)
                Problem = BackPackConfigProblem.Duplicates;
            else if (!HasExpected)
                Problem = BackPackConfigProblem.Misplaced;
            else
                Problem = BackPackConfigProblem.None;
        }

        public static BackPackConfigScan Run()
        {
            var paths = new List<string>();

            foreach (string guid in AssetDatabase.FindAssets("t:" + ConfigTypeName))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (IsConfig(path) && !paths.Contains(path))
                    paths.Add(path);
            }

            paths.Sort(StringComparer.Ordinal);
            return new BackPackConfigScan(paths);
        }

        // FindAssets matches by short class name — check the full one so a
        // same-named type from another namespace is not counted.
        public static bool IsConfig(string path) =>
            !string.IsNullOrEmpty(path) &&
            path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) &&
            AssetDatabase.GetMainAssetTypeAtPath(path)?.FullName == ConfigTypeFullName;

        public string Message
        {
            get
            {
                switch (Problem)
                {
                    case BackPackConfigProblem.Missing:
                        return $"BackPack config not found at {BackPackOptions.ConfigAssetPath}, so " +
                            "BackPack is off: no events are sent and the backpack window shows an error.";

                    case BackPackConfigProblem.Misplaced:
                        return $"BackPack config is at {Paths[0]}, but it must be exactly at " +
                            $"{BackPackOptions.ConfigAssetPath}: the game loads it only from there, so " +
                            "as it is BackPack is off. Move it (BackPack / Select Config Asset offers to).";

                    case BackPackConfigProblem.Duplicates:
                        var message = new StringBuilder();
                        message.Append($"Found {Paths.Count} BackPack configs, there must be exactly one, at ")
                            .Append(BackPackOptions.ConfigAssetPath).Append(':');

                        foreach (string path in Paths)
                            message.Append("\n  - ").Append(path)
                                .Append(path == BackPackOptions.ConfigAssetPath ? " (used)" : "");

                        message.Append(HasExpected
                            ? "\nThe others are not used by the game, yet look configured in the inspector. Delete them."
                            : "\nNone of them is at the expected path, so BackPack is off. Move the right one there and delete the rest.");

                        return message.ToString();

                    default:
                        return null;
                }
            }
        }

        public UnityEngine.Object[] LoadAll()
        {
            var assets = new List<UnityEngine.Object>();

            foreach (string path in Paths)
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(path);

                if (asset != null)
                    assets.Add(asset);
            }

            return assets.ToArray();
        }
    }

    // Complains in the Console as soon as a config appears in the wrong place or a
    // second one appears — not only when someone opens the Build Manager.
    internal sealed class BackPackConfigPostprocessor : AssetPostprocessor
    {
        private const string SessionCheckedKey = "LongLeap.Backpack.ConfigChecked";

        private static bool _scheduled;
        private static bool _reportMissing;

        // Once per editor session, so a problem left from before is not forgotten.
        // A missing config is not reported here: BackPack may be off on purpose,
        // and BackPackTask already says so at runtime.
        [InitializeOnLoadMethod]
        private static void CheckOncePerSession()
        {
            if (SessionState.GetBool(SessionCheckedKey, false))
                return;

            SessionState.SetBool(SessionCheckedKey, true);
            Schedule(reportMissing: false);
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            // Deleted and moved-away paths can no longer be typed — the only one
            // that matters is the expected path itself.
            bool expectedGone =
                Array.IndexOf(deletedAssets, BackPackOptions.ConfigAssetPath) >= 0 ||
                Array.IndexOf(movedFromAssetPaths, BackPackOptions.ConfigAssetPath) >= 0;

            if (expectedGone ||
                Array.Exists(importedAssets, BackPackConfigScan.IsConfig) ||
                Array.Exists(movedAssets, BackPackConfigScan.IsConfig))
            {
                Schedule(reportMissing: expectedGone);
            }
        }

        // Deferred: one report per batch of imports, and outside the import itself.
        private static void Schedule(bool reportMissing)
        {
            _reportMissing |= reportMissing;

            if (_scheduled)
                return;

            _scheduled = true;
            EditorApplication.delayCall += Report;
        }

        private static void Report()
        {
            bool reportMissing = _reportMissing;
            _scheduled = false;
            _reportMissing = false;

            var scan = BackPackConfigScan.Run();

            switch (scan.Problem)
            {
                case BackPackConfigProblem.Missing:
                    if (reportMissing)
                        UnityEngine.Debug.LogWarning(scan.Message);
                    break;

                case BackPackConfigProblem.Misplaced:
                case BackPackConfigProblem.Duplicates:
                    var context = AssetDatabase.LoadMainAssetAtPath(FirstStray(scan));
                    UnityEngine.Debug.LogError(scan.Message, context);
                    break;
            }
        }

        // The asset to ping on click in the Console: the one that should not be there.
        private static string FirstStray(BackPackConfigScan scan)
        {
            foreach (string path in scan.Paths)
                if (path != BackPackOptions.ConfigAssetPath)
                    return path;

            return scan.Paths[0];
        }
    }
}
