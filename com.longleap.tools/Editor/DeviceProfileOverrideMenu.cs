using UnityEditor;

namespace LongLeap.Tools.Editor
{
    /// <summary>
    /// Меню <c>Tools/Device Profile</c>: выбрать, каким устройством притворяется
    /// редактор. Нужно, чтобы увидеть мобильный или веб-вариант окна без Device Simulator
    /// и без сборки. Выбор хранится в EditorPrefs (у каждого разработчика свой, в репозиторий
    /// не попадает) и действует со следующего создания окон — перезапусти Play Mode.
    /// </summary>
    public static class DeviceProfileOverrideMenu
    {
        private const string Root = "Tools/Device Profile/";
        private const string AutoPath = Root + "Auto (detect device)";
        private const string DesktopPath = Root + "Desktop";
        private const string MobilePath = Root + "Mobile";
        private const string WebDesktopPath = Root + "Web Desktop";
        private const string WebMobilePath = Root + "Web Mobile";

        private static int Current
        {
            get => EditorPrefs.GetInt(DeviceInfo.EditorOverrideKey, -1);
            set => EditorPrefs.SetInt(DeviceInfo.EditorOverrideKey, value);
        }

        [MenuItem(AutoPath, false, 2300)]
        private static void SelectAuto() => Current = -1;

        [MenuItem(AutoPath, true)]
        private static bool ValidateAuto() => Check(AutoPath, -1);

        [MenuItem(DesktopPath, false, 2301)]
        private static void SelectDesktop() => Current = (int)DeviceProfile.Desktop;

        [MenuItem(DesktopPath, true)]
        private static bool ValidateDesktop() => Check(DesktopPath, (int)DeviceProfile.Desktop);

        [MenuItem(MobilePath, false, 2302)]
        private static void SelectMobile() => Current = (int)DeviceProfile.Mobile;

        [MenuItem(MobilePath, true)]
        private static bool ValidateMobile() => Check(MobilePath, (int)DeviceProfile.Mobile);

        [MenuItem(WebDesktopPath, false, 2303)]
        private static void SelectWebDesktop() => Current = (int)DeviceProfile.WebDesktop;

        [MenuItem(WebDesktopPath, true)]
        private static bool ValidateWebDesktop() => Check(WebDesktopPath, (int)DeviceProfile.WebDesktop);

        [MenuItem(WebMobilePath, false, 2304)]
        private static void SelectWebMobile() => Current = (int)DeviceProfile.WebMobile;

        [MenuItem(WebMobilePath, true)]
        private static bool ValidateWebMobile() => Check(WebMobilePath, (int)DeviceProfile.WebMobile);

        private static bool Check(string path, int value)
        {
            Menu.SetChecked(path, Current == value);
            return true;
        }
    }
}
