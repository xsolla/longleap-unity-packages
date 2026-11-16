using UnityEditor;

namespace LongLeap.Tools.Editor
{
    /// <summary>
    /// The <c>Tools/Device Profile</c> menu: choose which device the editor pretends to be.
    /// Lets you see the mobile or web variant of a window without the Device Simulator and
    /// without a build. The choice is stored in EditorPrefs (per developer and per project,
    /// never committed) and applies from the next time windows are built — restart Play Mode.
    /// While the choice is not Auto, entering Play Mode logs a warning.
    /// </summary>
    public static class DeviceProfileOverrideMenu
    {
        private const string Root = "Tools/Device Profile/";
        private const string AutoPath = Root + "Auto (detect device)";
        private const string DesktopPath = Root + "Desktop";
        private const string MobilePath = Root + "Mobile";
        private const string WebDesktopPath = Root + "Web Desktop";
        private const string WebMobilePath = Root + "Web Mobile";

        [MenuItem(AutoPath, false, 2300)]
        private static void SelectAuto() => DeviceInfo.EditorOverride = null;

        [MenuItem(AutoPath, true)]
        private static bool ValidateAuto() => Check(AutoPath, null);

        [MenuItem(DesktopPath, false, 2301)]
        private static void SelectDesktop() => DeviceInfo.EditorOverride = DeviceProfile.Desktop;

        [MenuItem(DesktopPath, true)]
        private static bool ValidateDesktop() => Check(DesktopPath, DeviceProfile.Desktop);

        [MenuItem(MobilePath, false, 2302)]
        private static void SelectMobile() => DeviceInfo.EditorOverride = DeviceProfile.Mobile;

        [MenuItem(MobilePath, true)]
        private static bool ValidateMobile() => Check(MobilePath, DeviceProfile.Mobile);

        [MenuItem(WebDesktopPath, false, 2303)]
        private static void SelectWebDesktop() => DeviceInfo.EditorOverride = DeviceProfile.WebDesktop;

        [MenuItem(WebDesktopPath, true)]
        private static bool ValidateWebDesktop() => Check(WebDesktopPath, DeviceProfile.WebDesktop);

        [MenuItem(WebMobilePath, false, 2304)]
        private static void SelectWebMobile() => DeviceInfo.EditorOverride = DeviceProfile.WebMobile;

        [MenuItem(WebMobilePath, true)]
        private static bool ValidateWebMobile() => Check(WebMobilePath, DeviceProfile.WebMobile);

        // Validate callback: ticks the item matching the current choice and keeps it enabled.
        private static bool Check(string path, DeviceProfile? value)
        {
            Menu.SetChecked(path, DeviceInfo.EditorOverride == value);
            return true;
        }
    }
}
