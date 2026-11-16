using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LongLeap.Tools
{
    /// <summary>
    /// The single answer to "what device is this": desktop, mobile, web on desktop or web
    /// on mobile. Detection lives here only — the rest of the code asks
    /// <see cref="Profile"/> / <see cref="IsMobile"/> / <see cref="IsWeb"/> and never checks
    /// <c>Application.isMobilePlatform</c>, the touchscreen or <c>UNITY_WEBGL</c> itself.
    ///
    /// Two axes:
    /// <list type="bullet">
    /// <item><b>Environment</b> — native build or browser (<see cref="IsWeb"/>).</item>
    /// <item><b>Controls</b> — touch or keyboard (<see cref="IsMobile"/>).</item>
    /// </list>
    ///
    /// A device is mobile if it is a mobile platform or a phone/tablet by device type.
    /// Beyond that, in the browser the primary pointer decides — the same media query
    /// <c>(hover: none) and (pointer: coarse)</c> the orientation overlay in index.html uses:
    /// a touchscreen laptop (primary pointer is a touchpad or mouse) stays WebDesktop, while an
    /// iPad with a desktop user agent becomes WebMobile. If the browser does not answer, the web
    /// fallback is the touchscreen (Keyboard always exists there, even on a phone). In a native
    /// build, mobile means a touchscreen without a keyboard.
    ///
    /// The value is not cached: a touchscreen or keyboard may appear after startup, and the
    /// check is cheap. Read a long-lived profile (choosing a window config) once.
    /// The in-game control scheme ("whatever was pressed last", world prompts) is a different
    /// question, answered by the game's input code.
    ///
    /// To test another profile: <see cref="Override"/> from code or the editor menu
    /// <c>Tools/Device Profile</c>.
    /// </summary>
    public static class DeviceInfo
    {
        /// <summary>
        /// Prefix of the EditorPrefs key for the editor menu choice; the key is this prefix plus
        /// <c>PlayerSettings.productGUID</c>, so the choice does not leak between projects.
        /// </summary>
        public const string EditorOverrideKeyPrefix = "LongLeap.Tools.DeviceProfileOverride";

        /// <summary>
        /// Profile forced from code — for tests and debug menus. null — not set.
        /// Takes precedence over the editor menu choice. Reset on entering Play Mode
        /// (see <see cref="ResetStatics"/>).
        /// </summary>
        public static DeviceProfile? Override { get; set; }

        /// <summary>The profile the game uses to choose configs and layout.</summary>
        public static DeviceProfile Profile => Override ?? EditorOverride ?? Detect();

        /// <summary>Running in a browser (WebGL), on desktop or mobile.</summary>
        public static bool IsWeb => Profile is DeviceProfile.WebDesktop or DeviceProfile.WebMobile;

        /// <summary>Touch controls: a mobile device, native or in the browser.</summary>
        public static bool IsMobile => Profile is DeviceProfile.Mobile or DeviceProfile.WebMobile;

        /// <summary>Keyboard and mouse controls, native or in the browser.</summary>
        public static bool IsDesktop => !IsMobile;

        /// <summary>
        /// Detect the profile from the device itself, ignoring <see cref="Override"/> and the
        /// editor menu. The editor menu uses it to show what "Auto" resolves to.
        /// </summary>
        public static DeviceProfile Detect()
        {
            bool web = Application.platform == RuntimePlatform.WebGLPlayer;
            bool mobile = DetectMobile(web);

            if (web)
                return mobile ? DeviceProfile.WebMobile : DeviceProfile.WebDesktop;

            return mobile ? DeviceProfile.Mobile : DeviceProfile.Desktop;
        }

        private static bool DetectMobile(bool web)
        {
            if (Application.isMobilePlatform || SystemInfo.deviceType == DeviceType.Handheld)
                return true;

            if (web)
            {
                int coarse = BrowserPrimaryPointerCoarse();
                if (coarse >= 0)
                    return coarse == 1;

                // The browser did not answer: Keyboard always exists, the touchscreen is the only signal.
                return Touchscreen.current != null;
            }

            return Touchscreen.current != null && Keyboard.current == null;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern int LongLeapToolsPrimaryPointerCoarse();
#endif

        // 1 — primary pointer is a finger without hover, 0 — mouse or touchpad, -1 — the browser did not answer.
        private static int BrowserPrimaryPointerCoarse()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return LongLeapToolsPrimaryPointerCoarse();
#else
            return -1;
#endif
        }

#if UNITY_EDITOR
        private static DeviceProfile? _editorOverride;
        private static bool _editorOverrideLoaded;

        /// <summary>
        /// Editor only: the choice in the <c>Tools/Device Profile</c> menu, null — Auto. Stored in
        /// EditorPrefs (per developer and per project) and read once per domain.
        /// </summary>
        public static DeviceProfile? EditorOverride
        {
            get
            {
                if (!_editorOverrideLoaded)
                {
                    _editorOverrideLoaded = true;
                    int value = UnityEditor.EditorPrefs.GetInt(EditorOverrideKey, -1);
                    _editorOverride = System.Enum.IsDefined(typeof(DeviceProfile), value)
                        ? (DeviceProfile?)value
                        : null;
                }

                return _editorOverride;
            }
            set
            {
                _editorOverride = value;
                _editorOverrideLoaded = true;
                UnityEditor.EditorPrefs.SetInt(EditorOverrideKey, value.HasValue ? (int)value.Value : -1);
            }
        }

        private static string EditorOverrideKey =>
            EditorOverrideKeyPrefix + "." + UnityEditor.PlayerSettings.productGUID;

        // A forgotten menu override is easy to miss: windows are built "wrong" for no visible reason.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void WarnAboutEditorOverride()
        {
            if (EditorOverride is { } forced)
                Debug.LogWarning($"DeviceInfo: profile is forced to {forced} by Tools/Device Profile. " +
                                 "Pick \"Auto\" there to detect the device.");
        }
#else
        private static DeviceProfile? EditorOverride => null;
#endif

        // Statics survive entering Play Mode without a domain reload: without this reset the
        // second run from the editor would start with the previous run's profile.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Override = null;
    }
}
