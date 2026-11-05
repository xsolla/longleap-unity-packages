namespace LongLeap.Settings
{
    /// <summary>
    /// Operations over the whole set of settings in a service. These are extensions rather than
    /// members of <see cref="ISettingsService"/>, so the interface stays unchanged for anyone who implements it.
    /// </summary>
    public static class SettingsServiceExtensions
    {
        /// <summary>
        /// Find a setting by its key (<see cref="Setting.Key"/>) across all groups of the service.
        /// Returns null if there is no such setting.
        /// </summary>
        public static Setting FindSetting(this ISettingsService service, string key)
        {
            if (service == null || string.IsNullOrEmpty(key)) return null;

            foreach (var group in service.Groups)
            {
                foreach (var setting in group.All)
                {
                    if (setting.Key == key)
                        return setting;
                }
            }

            return null;
        }

        /// <summary>
        /// Reset everything registered in the service to its default value.
        /// Groups are not listed by name, so a newly added group is reset automatically.
        /// Values go through <see cref="ISettingsService.SetValue{T}"/>, so
        /// <see cref="ISettingsService.OnChanged"/> subscribers are notified.
        /// </summary>
        public static void ResetToDefaults(this ISettingsService service)
        {
            if (service == null) return;

            foreach (var group in service.Groups)
            {
                foreach (var setting in group.All)
                    setting.ResetToDefault(service);
            }
        }
    }
}
