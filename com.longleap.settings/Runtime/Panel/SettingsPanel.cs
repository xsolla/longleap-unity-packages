using System;
using System.Collections.Generic;
using LongLeap.Tools;
using LongLeap.Tools.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LongLeap.Settings.UI
{
    /// <summary>
    /// Settings panel built in code inside the given <c>root</c>: backdrop, panel, sections with
    /// rows, and the "defaults" and "back" buttons. It is not tied to a window system: the game's
    /// window only creates the panel in its root and calls it at the right moments.
    ///
    /// What to show is decided by <see cref="SettingsWindowConfig"/>: sections, rows, captions,
    /// font and panel size. The config is picked from the set by the global device profile
    /// (<see cref="DeviceInfo.Profile"/>): desktop, mobile, web on desktop, web on mobile.
    /// A config row refers to a setting by key, and the row view depends on the setting type:
    /// number — slider (<see cref="SettingsVolumeRow"/>), bool — toggle
    /// (<see cref="SettingsToggleRow"/>), key — rebind button (<see cref="SettingsKeyRow"/>).
    ///
    /// There may be no settings (<see cref="Bind"/> not called yet); then the panel says that
    /// settings are unavailable, and the back button still works. A row with an unknown key logs
    /// an error and is skipped; the rest is built as usual.
    ///
    /// The panel does not write to storage: a slider fires dozens of times, and only the window
    /// knows the right moment to save (when it closes).
    /// </summary>
    public sealed class SettingsPanel : IDisposable
    {
        /// <summary>Section: header, column of rows and hint.</summary>
        private sealed class SectionView
        {
            public SettingsWindowConfig.Section Config;
            public GameObject Header;
            public RectTransform Column;
            public GameObject Hint;
            public int Rows;
        }

        private readonly RectTransform _root;
        private readonly Action _onBack;
        private readonly bool _touchOnly;
        private readonly SettingsWindowConfig _config;
        private readonly TMP_FontAsset _font;

        private readonly List<SectionView> _sections = new List<SectionView>();
        private readonly List<SettingsVolumeRow> _volumeRows = new List<SettingsVolumeRow>();
        private readonly List<SettingsToggleRow> _toggleRows = new List<SettingsToggleRow>();
        private readonly List<SettingsKeyRow> _keyRows = new List<SettingsKeyRow>();

        private ISettingsService _service;
        private TextMeshProUGUI _unavailable;
        private bool _rowsBuilt;

        /// <summary>
        /// Build the panel inside <paramref name="root"/>. Rows appear once settings arrive
        /// (<see cref="Bind"/>); before that there is nothing to show.
        /// </summary>
        /// <param name="root">Root stretched over the window; the backdrop and panel go inside it.</param>
        /// <param name="configs">Configs per device profile; one filled config is enough.</param>
        /// <param name="onBack">Called when "back" is pressed.</param>
        public SettingsPanel(
            RectTransform root,
            SettingsWindowConfigSet configs,
            Action onBack)
        {
            _root = root != null ? root : throw new ArgumentNullException(nameof(root));
            _onBack = onBack;
            var profile = DeviceInfo.Profile;
            _touchOnly = profile is DeviceProfile.Mobile or DeviceProfile.WebMobile;
            _config = ResolveConfig(configs, profile);
            _font = ResolveFont();

            BuildChrome();
        }

        // =========================================================
        // BINDING
        // =========================================================

        /// <summary>
        /// Give the panel its settings service. Called by the game once the service is ready;
        /// the panel does not know about any service container and cannot fetch it itself.
        /// </summary>
        public void Bind(ISettingsService service)
        {
            if (_service == service) return;

            if (_service != null)
                _service.OnChanged -= OnSettingChanged;

            _service = service;

            if (_service == null) return;

            _service.OnChanged += OnSettingChanged;

            BuildRows();
        }

        /// <summary>
        /// Show values that changed outside the panel (for example, a mute button elsewhere or
        /// loading a save). The window calls it when opened.
        /// </summary>
        public void Refresh()
        {
            foreach (var row in _volumeRows)
                if (row != null) row.Refresh();

            foreach (var row in _toggleRows)
                if (row != null) row.Refresh();

            foreach (var row in _keyRows)
                if (row != null) row.Refresh();
        }

        /// <summary>Stop listening for a key in all rows. The window calls it when closed.</summary>
        public void StopListening() => StopListening(null);

        public void Dispose()
        {
            if (_service != null)
                _service.OnChanged -= OnSettingChanged;

            _service = null;
        }

        // =========================================================
        // CONFIG AND FONT
        // =========================================================

        /// <summary>
        /// Config for the device, by the global profile (<see cref="DeviceInfo.Profile"/>).
        /// No config for the profile: take the nearest filled one
        /// (<see cref="SettingsWindowConfigSet.Resolve"/>). None at all: the panel still works
        /// with a title, a message and the back button, otherwise the menu could not be left.
        /// </summary>
        private SettingsWindowConfig ResolveConfig(SettingsWindowConfigSet configs, DeviceProfile profile)
        {
            var used = profile;
            var config = configs?.Resolve(profile, out used);

            if (config != null)
            {
                if (used != profile)
                {
                    Debug.LogWarning(
                        $"[Settings] The settings window has no config for profile {profile}: " +
                        $"using the config for {used}.", _root);
                }

                return config;
            }

            Debug.LogError(
                "[Settings] The settings window has no config assigned: no sections will be shown. " +
                "Assign a SettingsWindowConfig to at least one field of the window's config set.", _root);

            return ScriptableObject.CreateInstance<SettingsWindowConfig>();
        }

        /// <summary>
        /// Panel font. The config field is the main source, but it may be left empty, and TMP
        /// without a font breaks the layout when the project has no default font (TMP Essentials
        /// not imported). So the fallbacks are the TMP default font, then the font of any other
        /// UI text under the same canvas, which surely exists and matches the rest of the menu.
        /// </summary>
        private TMP_FontAsset ResolveFont()
        {
            if (_config.Font != null) return _config.Font;

            if (TMP_Settings.defaultFontAsset != null)
                return TMP_Settings.defaultFontAsset;

            var canvas = _root.GetComponentInParent<Canvas>();

            if (canvas != null)
            {
                foreach (var text in canvas.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (text != null && text.font != null)
                        return text.font;
                }
            }

            Debug.LogError(
                "[Settings] No font found: settings window captions will be empty. " +
                "Assign a font to the Font field of SettingsWindowConfig.", _root);

            return null;
        }

        // =========================================================
        // BUILDING
        // =========================================================

        /// <summary>
        /// Static part: backdrop, panel, section headers and buttons. Built right away, before
        /// settings arrive, otherwise a window opened without them would be an empty rectangle.
        /// Section rows are added later, in <see cref="BuildRows"/>, into the existing columns.
        /// </summary>
        private void BuildChrome()
        {
            var backdrop = RuntimeUi.CreateImage("Backdrop", _root, RuntimeUi.Backdrop, raycastTarget: true);
            backdrop.rectTransform.Stretch();

            var panel = RuntimeUi.CreateImage("Panel", _root, RuntimeUi.PanelBackground, raycastTarget: true);
            var panelRect = panel.rectTransform;
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = _config.PanelSize;

            RuntimeUi.AddColumn(panelRect, spacing: 12f, padding: new RectOffset(28, 28, 24, 24));

            var title = RuntimeUi.CreateText(
                "Title", panelRect, _font, _config.Title, 46, RuntimeUi.Accent, TextAlignmentOptions.Center);
            RuntimeUi.SetSize(title.gameObject, preferredHeight: 56f);

            _unavailable = RuntimeUi.CreateText(
                "Unavailable", panelRect, _font, _config.UnavailableText, RuntimeUi.LabelFontSize,
                RuntimeUi.TextDim, TextAlignmentOptions.Center);
            RuntimeUi.SetSize(_unavailable.gameObject, preferredHeight: 40f);

            // Touch devices have no keyboard: sections marked HideOnTouch are not created.
            foreach (var section in _config.Sections)
            {
                if (section == null) continue;
                if (section.HideOnTouch && _touchOnly) continue;

                _sections.Add(BuildSection(panelRect, section));
            }

            BuildFooter(panelRect);

            ShowUnavailable(true);
        }

        private SectionView BuildSection(RectTransform panel, SettingsWindowConfig.Section section)
        {
            var view = new SectionView { Config = section };

            view.Header = CreateSectionHeader(panel, section.Title);
            view.Column = CreateColumn(panel, "Rows");

            if (!string.IsNullOrEmpty(section.Hint))
            {
                var hint = RuntimeUi.CreateText(
                    "Hint", panel, _font, section.Hint, 18, RuntimeUi.TextDim, TextAlignmentOptions.Center);
                RuntimeUi.SetSize(hint.gameObject, preferredHeight: 28f);
                view.Hint = hint.gameObject;
            }

            return view;
        }

        private void BuildFooter(RectTransform panel)
        {
            var footer = RuntimeUi.CreateRect("Footer", panel);
            var layout = RuntimeUi.AddRow(footer, spacing: 16f);
            layout.childAlignment = TextAnchor.MiddleCenter;
            RuntimeUi.SetSize(footer.gameObject, preferredHeight: 54f);

            var reset = RuntimeUi.CreateButton(
                "Reset", footer, _font, _config.ResetText, new Color(1f, 1f, 1f, 0.10f), out _);
            RuntimeUi.SetSize(reset.gameObject, preferredWidth: 220f, preferredHeight: 48f);
            reset.onClick.AddListener(OnReset);

            var back = RuntimeUi.CreateButton(
                "Back", footer, _font, _config.BackText, RuntimeUi.Accent, out _);
            RuntimeUi.SetSize(back.gameObject, preferredWidth: 220f, preferredHeight: 48f);
            back.onClick.AddListener(() => _onBack?.Invoke());
        }

        private GameObject CreateSectionHeader(RectTransform panel, string caption)
        {
            var header = RuntimeUi.CreateText("Section", panel, _font, caption, 26, RuntimeUi.Accent);
            RuntimeUi.SetSize(header.gameObject, preferredHeight: 32f);

            return header.gameObject;
        }

        private static RectTransform CreateColumn(RectTransform panel, string name)
        {
            var column = RuntimeUi.CreateRect(name, panel);
            RuntimeUi.AddColumn(column, RuntimeUi.RowSpacing);
            RuntimeUi.SetSize(column.gameObject, preferredHeight: 0f);

            return column;
        }

        /// <summary>
        /// Rows appear together with the settings; before that there is nothing to show.
        /// Walks the config sections; a row with an unknown key or an unsupported setting type is
        /// skipped with an error, and the other rows are not affected.
        /// </summary>
        private void BuildRows()
        {
            if (_rowsBuilt || _service == null) return;

            _rowsBuilt = true;

            foreach (var section in _sections)
            {
                foreach (var row in section.Config.Rows)
                {
                    if (row != null && AddRow(section, row))
                        section.Rows++;
                }

                FitColumn(section.Column, section.Rows);

                // A section without a single working row is an empty header: hide it.
                if (section.Rows == 0)
                {
                    section.Header.SetActive(false);

                    if (section.Hint != null)
                        section.Hint.SetActive(false);
                }
            }

            ShowUnavailable(false);
            Refresh();
        }

        /// <summary>Create a row for a config entry. Returns false if no row was created.</summary>
        private bool AddRow(SectionView section, SettingsWindowConfig.Row row)
        {
            var setting = _service.FindSetting(row.SettingKey);

            if (setting == null)
            {
                Debug.LogError(
                    $"[Settings] Settings window config (section '{section.Config.Title}'): " +
                    $"no setting with key '{row.SettingKey}', row skipped. " +
                    "Pick a key from the dropdown in SettingsWindowConfig.", _root);

                return false;
            }

            string label = row.Label;

            switch (setting)
            {
                case NumericSetting<float> number:
                {
                    var view = SettingsVolumeRow.Create(section.Column, _font, label);
                    view.Bind(_service, number);
                    _volumeRows.Add(view);
                    return true;
                }

                case NumericSetting<int> number:
                {
                    var view = SettingsVolumeRow.Create(section.Column, _font, label);
                    view.Bind(_service, number);
                    _volumeRows.Add(view);
                    return true;
                }

                case Setting<bool> toggle:
                {
                    var view = SettingsToggleRow.Create(section.Column, _font, label);
                    view.Bind(_service, toggle);
                    _toggleRows.Add(view);
                    return true;
                }

                case Setting<Key> binding:
                {
                    var view = SettingsKeyRow.Create(section.Column, _font, label, _config.ListeningText);
                    view.Bind(_service, binding);
                    view.ListenRequested += StopListening;
                    _keyRows.Add(view);
                    return true;
                }

                default:
                    Debug.LogError(
                        $"[Settings] No row view for setting '{setting.Key}' " +
                        $"(type {setting.ValueType.Name}): row skipped.", _root);
                    return false;
            }
        }

        /// <summary>
        /// The column takes exactly the height its rows need: the panel has a fixed height, and a
        /// column without its own size would collapse.
        /// </summary>
        private static void FitColumn(RectTransform column, int rows)
        {
            if (column == null) return;

            float height = rows > 0
                ? rows * RuntimeUi.RowHeight + (rows - 1) * RuntimeUi.RowSpacing
                : 0f;

            RuntimeUi.SetSize(column.gameObject, preferredHeight: height);
        }

        private void ShowUnavailable(bool show)
        {
            if (_unavailable != null)
                _unavailable.gameObject.SetActive(show);
        }

        // =========================================================
        // HANDLERS
        // =========================================================

        /// <summary>Only one row may listen for a key; stop the others.</summary>
        private void StopListening(SettingsKeyRow except)
        {
            foreach (var row in _keyRows)
            {
                if (row == null || row == except) continue;

                row.StopListening();
            }
        }

        /// <summary>
        /// A value changed in the service. Refresh all rows, not just one: a key rebind also
        /// changes the row the key was taken from.
        /// </summary>
        private void OnSettingChanged(Setting setting) => Refresh();

        private void OnReset()
        {
            if (_service == null) return;

            StopListening(null);

            _service.ResetToDefaults();

            Refresh();
        }
    }
}
