using System;
using System.Threading.Tasks;
using LongLeap.Settings;
using LongLeap.Settings.UI;
using UnityEngine;

namespace LongLeap.UI
{
    /// <summary>
    /// Окно настроек — оконная обёртка над <see cref="SettingsPanel"/> из пакета настроек.
    ///
    /// Окно ничего не собирает само: в префабе у него только пустой Root, панель
    /// строится в нём кодом. Что показать — секции, строки, подписи, шрифт и размер —
    /// решает <see cref="SettingsWindowConfig"/>, а какой из конфигов взять, решает
    /// профиль устройства (ПК, мобильный, веб на ПК, веб на мобильном) — см.
    /// <see cref="SettingsWindowConfigSet"/>. Другая раскладка под платформу —
    /// другой конфиг, а не правка кода.
    ///
    /// Сервис настроек окну приносит загрузка (<see cref="Bind"/>). Настроек может
    /// не быть — тогда окно открывается и честно говорит, что настройки недоступны:
    /// кнопка возврата работает в любом случае.
    ///
    /// Значение применяется сразу, пока тянут ползунок; в хранилище всё уезжает
    /// на закрытии окна — одной записью, а не на каждое движение.
    /// </summary>
    public sealed class SettingsWindow : MenuPageWindow
    {
        [Tooltip("Конфиги окна по профилям устройства. Достаточно одного: " +
                 "незаполненный профиль берёт ближайший заполненный.")]
        [SerializeField] private SettingsWindowConfigSet _configs = new SettingsWindowConfigSet();

        private SettingsPanel _panel;
        private Func<Task> _save;

        public Func<Task> ReturnToPause { get; set; }

        // =========================================================
        // ПОДКЛЮЧЕНИЕ
        // =========================================================

        /// <summary>
        /// Отдать окну настройки игры. Зовёт загрузка, когда UI уже поднят: пакет UI
        /// не знает про контейнер сервисов и сам их не достанет.
        /// </summary>
        /// <param name="service">Сервис настроек: строки панели пишут в него и слушают его.</param>
        /// <param name="save">Сохранение на закрытии окна. Null — не сохраняем.</param>
        public void Bind(ISettingsService service, Func<Task> save = null)
        {
            _save = save;
            _panel.Bind(service);
        }

        // =========================================================
        // WINDOW
        // =========================================================

        public override async Task BackAsync()
        {
            if (ReturnToPause == null)
            {
                await base.BackAsync();
                return;
            }

            var returnAction = ReturnToPause;
            ReturnToPause = null;
            await CloseSelfAsync();
            await returnAction();
        }

        public override Task OpenAsync()
        {
            // Значения могли поменяться мимо окна: кнопкой mute в главном меню
            // или чтением сейва.
            _panel.Refresh();

            return base.OpenAsync();
        }

        /// <summary>
        /// Закрытие — момент записи: пока окно открыто, ползунок дёргается
        /// десятки раз, и писать в хранилище на каждое движение незачем.
        /// </summary>
        public override async Task CloseAsync()
        {
            _panel.StopListening();

            await base.CloseAsync();

            if (_save != null)
                Forget(_save());
        }

        protected override void Awake()
        {
            base.Awake();

            _panel = new SettingsPanel((RectTransform)Root.transform, _configs, OnBackClicked);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            _panel?.Dispose();
        }

        private void OnBackClicked() => Forget(BackAsync());
    }
}
