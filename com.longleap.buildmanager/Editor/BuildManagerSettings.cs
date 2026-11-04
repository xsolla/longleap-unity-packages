using System.IO;
using LongLeap.Core.Editor;
using UnityEditor;
using UnityEngine;

namespace LongLeap.BuildManager.Editor
{
    // Project-wide Build Manager settings, stored in ProjectSettings/ so they
    // are committed and shared by the team (EditorPrefs would be per machine).
    // Edited under Project Settings / LongLeap / Build Manager.
    [FilePath("ProjectSettings/LongLeapBuildManagerSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class BuildManagerSettings : ScriptableSingleton<BuildManagerSettings>
    {
        public const string DefaultWebGLOutputPath = "Builds/WebGL";
        public const string SettingsPath = "Project/LongLeap/Build Manager";

        [SerializeField] private string _webGLOutputPath = DefaultWebGLOutputPath;

        // The folder the WebGL player build writes to, relative to the project
        // root or absolute. Never empty: an empty or whitespace value falls
        // back to the default.
        public string WebGLOutputPath =>
            string.IsNullOrWhiteSpace(_webGLOutputPath)
                ? DefaultWebGLOutputPath
                : _webGLOutputPath.Trim().Replace('\\', '/').TrimEnd('/');

        public void SetWebGLOutputPath(string value)
        {
            if (value == _webGLOutputPath)
                return;

            _webGLOutputPath = value;
            Save(true);

            // The build target's label shows the path and is cached by Core.
            ProjectBuildTargets.Invalidate();
        }

        // Why a path cannot be used, or null. A build inside Assets/ would be
        // imported back into the project; one that IS the project root (or
        // above it) would overwrite it.
        public static string ValidateOutputPath(string path)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            string full = Path.GetFullPath(Path.Combine(projectRoot, path)).Replace('\\', '/').TrimEnd('/');

            string assets = projectRoot + "/Assets";

            if (full == assets || full.StartsWith(assets + "/", System.StringComparison.OrdinalIgnoreCase))
                return $"The WebGL output folder '{path}' is inside Assets/, so Unity would import the build. " +
                       "Pick a folder outside Assets/ in Project Settings / LongLeap / Build Manager.";

            if (string.Equals(full, projectRoot, System.StringComparison.OrdinalIgnoreCase)
                || projectRoot.StartsWith(full + "/", System.StringComparison.OrdinalIgnoreCase))
                return $"The WebGL output folder '{path}' is the project folder or above it. " +
                       "Pick a subfolder in Project Settings / LongLeap / Build Manager.";

            return null;
        }

        [SettingsProvider]
        private static SettingsProvider CreateProvider() =>
            new SettingsProvider(SettingsPath, SettingsScope.Project)
            {
                label = "Build Manager",
                keywords = new[] { "build", "output", "webgl", "folder", "path" },
                guiHandler = _ => DrawSettings(),
            };

        private static void DrawSettings()
        {
            BuildManagerSettings settings = instance;

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();

                string path = EditorGUILayout.DelayedTextField(
                    new GUIContent(
                        "WebGL Output Folder",
                        "Where the WebGL player build target writes the build. Relative to the " +
                        "project folder, or absolute. Another build target may pick its own folder."),
                    settings._webGLOutputPath);

                if (EditorGUI.EndChangeCheck())
                    settings.SetWebGLOutputPath(path);

                if (GUILayout.Button("Browse…", GUILayout.Width(80f)))
                {
                    string picked = EditorUtility.OpenFolderPanel(
                        "WebGL Output Folder", settings.WebGLOutputPath, string.Empty);

                    if (!string.IsNullOrEmpty(picked))
                        settings.SetWebGLOutputPath(ToProjectRelative(picked));

                    GUIUtility.ExitGUI();
                }

                if (GUILayout.Button("Reset", GUILayout.Width(60f)))
                    settings.SetWebGLOutputPath(DefaultWebGLOutputPath);
            }

            string problem = ValidateOutputPath(settings.WebGLOutputPath);

            if (problem != null)
                EditorGUILayout.HelpBox(problem, MessageType.Error);
        }

        // Keeps a picked folder relative when it is under the project, so the
        // committed setting works on every machine.
        private static string ToProjectRelative(string path)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            string full = Path.GetFullPath(path).Replace('\\', '/');

            return full.StartsWith(projectRoot + "/", System.StringComparison.OrdinalIgnoreCase)
                ? full.Substring(projectRoot.Length + 1)
                : full;
        }
    }
}
