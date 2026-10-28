namespace LongLeap.Settings
{
    /// <summary>
    /// Операции над всем набором настроек сервиса. Расширениями, а не членами
    /// <see cref="ISettingsService"/>: интерфейс остаётся прежним для тех, кто его реализует сам.
    /// </summary>
    public static class SettingsServiceExtensions
    {
        /// <summary>
        /// Найти настройку по ключу (<see cref="Setting.Key"/>) среди всех групп сервиса.
        /// null — такой настройки нет.
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
        /// Вернуть всё, что зарегистрировано в сервисе, к значениям по умолчанию.
        /// Группы не перечисляются поимённо: новая группа сбрасывается сама.
        /// Значения идут через <see cref="ISettingsService.SetValue{T}"/>, поэтому
        /// подписчики <see cref="ISettingsService.OnChanged"/> узнают об изменениях.
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
