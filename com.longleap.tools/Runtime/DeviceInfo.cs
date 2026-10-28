using UnityEngine;
using UnityEngine.InputSystem;

namespace LongLeap.Tools
{
    /// <summary>
    /// Глобальный ответ на вопрос «что это за устройство»: ПК, мобильное, веб на ПК
    /// или веб на мобильном. Определение живёт только здесь — остальной код спрашивает
    /// <see cref="Profile"/> / <see cref="IsMobile"/> / <see cref="IsWeb"/> и не
    /// проверяет <c>Application.isMobilePlatform</c>, тачскрин или <c>UNITY_WEBGL</c> сам.
    ///
    /// Две оси:
    /// <list type="bullet">
    /// <item><b>Среда</b> — нативная сборка или браузер (<see cref="IsWeb"/>).</item>
    /// <item><b>Управление</b> — пальцами или клавиатурой (<see cref="IsMobile"/>).</item>
    /// </list>
    ///
    /// Мобильным устройство считается, если это мобильная платформа или телефон/планшет
    /// по типу устройства, а иначе — если есть тачскрин. В браузере Input System создаёт
    /// Keyboard всегда, даже на телефоне, поэтому там «нет клавиатуры» — не признак:
    /// есть тачскрин — считаем устройство мобильным. На ноутбуке с тачскрином в браузере
    /// это даст WebMobile — осознанный компромисс.
    ///
    /// Значение не кэшируется: тачскрин и клавиатура могут появиться после старта, а
    /// вычисление дешёвое. Профиль, нужный надолго (выбор конфига окна), читай один раз.
    /// Игровая схема управления «чем нажали последним» (подсказки в мире) — другой вопрос,
    /// её определяет игровой ввод.
    ///
    /// Для проверки другого профиля: <see cref="Override"/> из кода или меню редактора
    /// <c>Tools/Device Profile</c>.
    /// </summary>
    public static class DeviceInfo
    {
        /// <summary>Ключ EditorPrefs для выбора из меню редактора. -1 — определять по устройству.</summary>
        public const string EditorOverrideKey = "LongLeap.Tools.DeviceProfileOverride";

        /// <summary>
        /// Принудительный профиль из кода — для тестов и отладочных меню. null — не задан.
        /// Сильнее выбора в меню редактора. Сбрасывается при входе в Play Mode
        /// (см. <see cref="ResetStatics"/>).
        /// </summary>
        public static DeviceProfile? Override { get; set; }

        /// <summary>Профиль, по которому игра выбирает конфиги и раскладку.</summary>
        public static DeviceProfile Profile => Override ?? EditorOverride() ?? Detect();

        public static bool IsWeb => Profile is DeviceProfile.WebDesktop or DeviceProfile.WebMobile;

        /// <summary>Управление пальцами: мобильное устройство, нативное или в браузере.</summary>
        public static bool IsMobile => Profile is DeviceProfile.Mobile or DeviceProfile.WebMobile;

        /// <summary>Управление клавиатурой и мышью, нативное или в браузере.</summary>
        public static bool IsDesktop => !IsMobile;

        /// <summary>
        /// Определить профиль по самому устройству, без <see cref="Override"/> и меню
        /// редактора. Нужен меню редактора, чтобы показать «Auto».
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

            if (Touchscreen.current == null)
                return false;

            // Браузер: Keyboard есть всегда, тачскрин — единственный признак.
            if (web)
                return true;

            return Keyboard.current == null;
        }

        private static DeviceProfile? EditorOverride()
        {
#if UNITY_EDITOR
            int value = UnityEditor.EditorPrefs.GetInt(EditorOverrideKey, -1);

            if (System.Enum.IsDefined(typeof(DeviceProfile), value))
                return (DeviceProfile)value;
#endif
            return null;
        }

        // Статика переживает вход в Play Mode без перезагрузки домена: без сброса
        // второй запуск из редактора начинал бы с профилем от прошлого.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Override = null;
    }
}
