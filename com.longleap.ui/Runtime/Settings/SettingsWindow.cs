using System;
using System.Threading.Tasks;
using LongLeap.Settings;
using LongLeap.Settings.UI;
using UnityEngine;

namespace LongLeap.UI
{
    /// <summary>
    /// Settings window — a window wrapper around <see cref="SettingsPanel"/> from the settings package.
    ///
    /// The window builds nothing itself: its prefab has only an empty Root, and the panel
    /// is built inside it in code. What to show — sections, rows, labels, font and size —
    /// is decided by <see cref="SettingsWindowConfig"/>, and which config to use is decided
    /// by the device profile (PC, mobile, web on PC, web on mobile) — see
    /// <see cref="SettingsWindowConfigSet"/>. A different layout for a platform means
    /// a different config, not a code change.
    ///
    /// The settings service is passed in by the game's loading sequence (<see cref="Bind"/>).
    /// Settings may be missing — then the window still opens and plainly says settings are
    /// unavailable: the back button works either way.
    ///
    /// Values apply immediately while the slider is dragged; everything is written to storage
    /// when the window closes — in a single write, not on every movement.
    /// </summary>
    public sealed class SettingsWindow : MenuPageWindow
    {
        [Tooltip("Window configs per device profile. One is enough: " +
                 "an empty profile falls back to the nearest filled one.")]
        [SerializeField] private SettingsWindowConfigSet _configs = new SettingsWindowConfigSet();

        private SettingsPanel _panel;
        private Func<Task> _save;

        public Func<Task> ReturnToPause { get; set; }

        // =========================================================
        // BINDING
        // =========================================================

        /// <summary>
        /// Give the window the game settings. Called by the game's loading sequence once the UI
        /// is up: the UI package doesn't know about the service container and can't fetch them itself.
        /// </summary>
        /// <param name="service">Settings service: panel rows write to it and listen to it.</param>
        /// <param name="save">Save on window close. Null means no saving.</param>
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
            // Values may have changed outside the window: via the mute button in the main menu
            // or by loading a save.
            _panel.Refresh();

            return base.OpenAsync();
        }

        /// <summary>
        /// Closing is when we write: while the window is open, a slider moves
        /// dozens of times, and there's no point writing to storage on every movement.
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
