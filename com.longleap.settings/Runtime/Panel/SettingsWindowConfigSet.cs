using System;
using LongLeap.Tools;
using UnityEngine;

namespace LongLeap.Settings.UI
{
    /// <summary>
    /// Конфиги окна настроек по профилям устройства (<see cref="DeviceProfile"/>).
    /// Заполнять нужно не все: незаполненный профиль берёт ближайший заполненный
    /// (<see cref="Resolve"/>), так что достаточно одного конфига.
    /// </summary>
    [Serializable]
    public sealed class SettingsWindowConfigSet
    {
        [Tooltip("ПК, нативная сборка.")]
        public SettingsWindowConfig Desktop;

        [Tooltip("Телефон или планшет, нативная сборка.")]
        public SettingsWindowConfig Mobile;

        [Tooltip("Браузер на ПК. Пусто — конфиг для ПК.")]
        public SettingsWindowConfig WebDesktop;

        [Tooltip("Браузер на телефоне или планшете. Пусто — мобильный конфиг.")]
        public SettingsWindowConfig WebMobile;

        /// <summary>
        /// Конфиг для профиля. Порядок запасных: веб берёт свой нативный аналог
        /// (WebMobile — Mobile), затем то же управление, затем остальное; так на
        /// мобильном без мобильного конфига окно соберётся по конфигу ПК, как и раньше.
        /// null — не заполнен ни один.
        /// </summary>
        public SettingsWindowConfig Resolve(DeviceProfile profile, out DeviceProfile used)
        {
            foreach (var candidate in Order(profile))
            {
                var config = Get(candidate);

                if (config == null) continue;

                used = candidate;
                return config;
            }

            used = profile;
            return null;
        }

        private SettingsWindowConfig Get(DeviceProfile profile) => profile switch
        {
            DeviceProfile.Desktop => Desktop,
            DeviceProfile.Mobile => Mobile,
            DeviceProfile.WebDesktop => WebDesktop,
            DeviceProfile.WebMobile => WebMobile,
            _ => null,
        };

        private static DeviceProfile[] Order(DeviceProfile profile) => profile switch
        {
            DeviceProfile.Mobile => new[]
            {
                DeviceProfile.Mobile, DeviceProfile.WebMobile, DeviceProfile.Desktop, DeviceProfile.WebDesktop,
            },
            DeviceProfile.WebDesktop => new[]
            {
                DeviceProfile.WebDesktop, DeviceProfile.Desktop, DeviceProfile.WebMobile, DeviceProfile.Mobile,
            },
            DeviceProfile.WebMobile => new[]
            {
                DeviceProfile.WebMobile, DeviceProfile.Mobile, DeviceProfile.WebDesktop, DeviceProfile.Desktop,
            },
            _ => new[]
            {
                DeviceProfile.Desktop, DeviceProfile.WebDesktop, DeviceProfile.Mobile, DeviceProfile.WebMobile,
            },
        };
    }
}
