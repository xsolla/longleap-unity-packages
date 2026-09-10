using System;
using UnityEngine;

namespace LongLeap.Localization
{
    /// <summary>
    /// Static convenience facade over <see cref="ILocalizationService"/>.
    /// Safely returns the key as a fallback if the service is not initialized.
    /// </summary>
    public static class Loc
    {
        private static ILocalizationService _service;

        /// <summary>
        /// Raised when the active locale changes.
        /// </summary>
        public static event Action OnLanguageChanged;

        /// <summary>
        /// Переводы доступны: сервис поднят и таблицы активного языка загружены.
        /// false — либо бутстрап ещё не дошёл до локализации, либо идёт смена
        /// языка. Пока false, Resolve отдаёт ключи, и текст по ним ставить рано.
        /// </summary>
        public static bool IsReady => _service != null && _service.IsReady;

        /// <summary>
        /// Initializes the static facade with a localization service instance.
        /// Called once at boot.
        /// </summary>
        public static void Init(ILocalizationService service)
        {
            if (service == null)
                throw new ArgumentNullException(nameof(service));

            if (_service != null)
                _service.OnLanguageChanged -= NotifyLanguageChanged;

            _service = service;
            _service.OnLanguageChanged += NotifyLanguageChanged;

            // Обновляем компоненты, которые включились до готовности сервиса.
            NotifyLanguageChanged();
        }

        /// <summary>
        /// Отцепляет сервис — перед перезагрузкой сцены, вместе с его Shutdown.
        /// Без этого фасад держал бы сервис уже уничтоженной сцены: его таблицы
        /// выгружены, а хост корутин мёртв, и следующая смена языка пришла бы в
        /// никуда. Подписчиков не дёргаем: они умирают в этот же момент.
        ///
        /// Чужой сервис не сбрасываем: если сцену уже подняли заново и на фасаде
        /// стоит новый сервис, запоздавший Reset от старого обнулил бы живую
        /// локализацию.
        /// </summary>
        public static void Reset(ILocalizationService service)
        {
            if (service == null || _service != service)
                return;

            _service.OnLanguageChanged -= NotifyLanguageChanged;
            _service = null;
        }

        // Статика переживает вход в Play Mode без перезагрузки домена: без сброса
        // второй запуск из редактора начинал бы с сервисом от прошлого.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _service = null;
            OnLanguageChanged = null;
        }

        private static void NotifyLanguageChanged() => OnLanguageChanged?.Invoke();

        /// <summary>
        /// Resolves a key via the initialized <see cref="ILocalizationService"/>.
        /// Returns the key itself (or <see cref="string.Empty"/> for null)
        /// when the service is unavailable.
        /// </summary>
        public static string Resolve(string key)
        {
            if (_service == null)
                return key ?? string.Empty;

            return _service.Resolve(key);
        }

        /// <summary>
        /// Resolves and formats a key via the initialized <see cref="ILocalizationService"/>.
        /// Returns the raw key as a fallback when the service is unavailable.
        /// </summary>
        public static string ResolveFormat(string key, params object[] args)
        {
            if (_service != null)
                return _service.ResolveFormat(key, args);

            // Сервиса нет — подставляем в сам ключ. Он подстановок не содержит,
            // но лишняя скобка в нём всё равно уронила бы string.Format.
            try
            {
                return string.Format(Resolve(key), args);
            }
            catch (FormatException)
            {
                return Resolve(key);
            }
        }
    }
}
