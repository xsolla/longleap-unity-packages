using System;
using LongLeap.Tools;
using UnityEngine;

namespace LongLeap.Settings.UI
{
    /// <summary>
    /// Settings window configs per device profile (<see cref="DeviceProfile"/>).
    /// Not all of them need to be filled: an empty profile takes the nearest filled one
    /// (<see cref="Resolve"/>), so a single config is enough.
    /// </summary>
    [Serializable]
    public sealed class SettingsWindowConfigSet
    {
        [Tooltip("Desktop, native build.")]
        public SettingsWindowConfig Desktop;

        [Tooltip("Phone or tablet, native build.")]
        public SettingsWindowConfig Mobile;

        [Tooltip("Desktop browser. Empty: the Desktop config.")]
        public SettingsWindowConfig WebDesktop;

        [Tooltip("Mobile browser. Empty: the Mobile config.")]
        public SettingsWindowConfig WebMobile;

        /// <summary>
        /// Config for the profile. Fallback order: a web profile takes its native counterpart
        /// (WebMobile → Mobile), then the same input type, then the rest; so a mobile device with no
        /// mobile config still builds the window from the desktop config.
        /// Returns null if none is filled.
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
