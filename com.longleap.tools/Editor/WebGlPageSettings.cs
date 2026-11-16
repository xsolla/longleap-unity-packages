using System.IO;
using UnityEditor;
using UnityEngine;

namespace LongLeap.Tools.Editor
{
    /// <summary>
    /// WebGL page settings for <see cref="WebGlPagePostprocessor"/>. Stored in
    /// <c>ProjectSettings/LongLeapWebGlPage.asset</c> and committed, so the build does not
    /// depend on whose machine (or CI) built it. The choice used to live in EditorPrefs — it is
    /// moved here once, on the first access while the file does not exist yet.
    /// </summary>
    [FilePath("ProjectSettings/LongLeapWebGlPage.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class WebGlPageSettings : ScriptableSingleton<WebGlPageSettings>
    {
        // Old EditorPrefs keys (productGUID is appended) — used only for the one-time migration.
        private const string LegacyModeKey = "WebGlPagePostprocessor.Mode.";
        private const string LegacyOrientationKey = "WebGlPagePostprocessor.Orientation.";

        [SerializeField]
        private WebGlPagePostprocessor.PageMode _mode = WebGlPagePostprocessor.PageMode.FixedAspect;

        [SerializeField]
        private WebGlPagePostprocessor.PageOrientation _orientation = WebGlPagePostprocessor.PageOrientation.Auto;

        [SerializeField]
        private bool _mobilePixelRatioOne = true;

        private static bool _legacyChecked;

        /// <summary>
        /// The project settings. Use this instead of <c>instance</c>: it also migrates the old
        /// choice from EditorPrefs.
        /// </summary>
        public static WebGlPageSettings Get()
        {
            if (!_legacyChecked)
            {
                _legacyChecked = true;

                if (!File.Exists(GetFilePath()))
                    instance.ImportLegacyEditorPrefs();
            }

            return instance;
        }

        /// <summary>Page mode (Fixed / Free Aspect). Saved to the settings file on change.</summary>
        public WebGlPagePostprocessor.PageMode Mode
        {
            get => _mode;
            set
            {
                if (_mode == value)
                    return;

                _mode = value;
                Save(true);
            }
        }

        /// <summary>Game orientation (Auto / Landscape / Portrait). Saved to the settings file on change.</summary>
        public WebGlPagePostprocessor.PageOrientation Orientation
        {
            get => _orientation;
            set
            {
                if (_orientation == value)
                    return;

                _orientation = value;
                Save(true);
            }
        }

        /// <summary>
        /// Uncomment <c>config.devicePixelRatio = 1</c> in the template: on phones the canvas
        /// renders in CSS pixels — faster, but blurrier on high-density screens.
        /// </summary>
        public bool MobilePixelRatioOne
        {
            get => _mobilePixelRatioOne;
            set
            {
                if (_mobilePixelRatioOne == value)
                    return;

                _mobilePixelRatioOne = value;
                Save(true);
            }
        }

        private void ImportLegacyEditorPrefs()
        {
            string modeKey = LegacyModeKey + PlayerSettings.productGUID;
            string orientationKey = LegacyOrientationKey + PlayerSettings.productGUID;

            if (!EditorPrefs.HasKey(modeKey) && !EditorPrefs.HasKey(orientationKey))
                return;

            _mode = (WebGlPagePostprocessor.PageMode)EditorPrefs.GetInt(modeKey, (int)_mode);
            _orientation = (WebGlPagePostprocessor.PageOrientation)EditorPrefs.GetInt(orientationKey, (int)_orientation);
            Save(true);

            Debug.Log($"WebGlPageSettings: page mode {_mode} and orientation {_orientation} moved from EditorPrefs " +
                      $"to {GetFilePath()}. Commit this file so every build uses them.");
        }
    }
}
