using System;
using LongLeap.Localization;
using LongLeap.Settings;
using LongLeap.Tools.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

namespace LongLeap.Settings.UI
{
    /// <summary>
    /// Key binding row: the action label and a button showing the current key.
    ///
    /// Clicking the button makes the row listen for a key and highlights it. The next key pressed
    /// becomes the new binding; Esc cancels. A key that is already in use is not lost: the two
    /// actions swap keys (see <see cref="Rebind"/>), so no action is left unbound after a rebind.
    ///
    /// Only one row can listen at a time: when a row starts listening it raises
    /// <see cref="ListenRequested"/>, and the panel stops the others.
    /// </summary>
    public sealed class SettingsKeyRow : MonoBehaviour
    {
        /// <summary>This row started listening for a key; the others should stop.</summary>
        public event Action<SettingsKeyRow> ListenRequested;

        private ISettingsService _service;
        private Setting<Key> _binding;

        private Button _button;
        private Image _buttonBackground;
        private TextMeshProUGUI _label;
        private TextMeshProUGUI _keyLabel;

        private string _labelKey;
        private string _fallbackLabel;
        private string _listeningText = "PRESS A KEY";

        private bool _listening;

        /// <summary>
        /// Frame in which listening started: the press that opened listening (Enter or Space on
        /// the button itself) must not immediately become the new key.
        /// </summary>
        private int _listenFrame;

        private Color _idleColor;

        /// <summary>Build the row inside a panel column.</summary>
        public static SettingsKeyRow Create(
            Transform parent, TMP_FontAsset font, string fallbackLabel, string listeningText)
        {
            var rect = RuntimeUi.CreateRect("KeyRow", parent);
            var row = rect.gameObject.AddComponent<SettingsKeyRow>();

            var background = rect.gameObject.AddComponent<Image>();
            background.color = RuntimeUi.RowBackground;
            background.raycastTarget = false;

            RuntimeUi.AddRow(rect, spacing: 14f);
            RuntimeUi.SetSize(rect.gameObject, preferredHeight: RuntimeUi.RowHeight);

            row._fallbackLabel = fallbackLabel;
            row._listeningText = listeningText;

            row._label = RuntimeUi.CreateText(
                "Label", rect, font, fallbackLabel, RuntimeUi.LabelFontSize, RuntimeUi.Text);
            RuntimeUi.SetSize(row._label.gameObject, flexibleWidth: 1f);

            row._idleColor = new Color(1f, 1f, 1f, 0.10f);
            row._button = RuntimeUi.CreateButton(
                "Key", rect, font, "-", row._idleColor, out row._keyLabel);
            row._buttonBackground = row._button.GetComponent<Image>();
            RuntimeUi.SetSize(row._button.gameObject, preferredWidth: 240f, preferredHeight: 34f);

            row._button.onClick.AddListener(row.OnClicked);

            return row;
        }

        /// <summary>Bind the row to an action's key setting.</summary>
        public void Bind(ISettingsService service, Setting<Key> binding)
        {
            _service = service;
            _binding = binding;
            _labelKey = binding != null ? binding.LabelKey : null;

            ShowLabel();
            Refresh();

            Loc.OnLanguageChanged += ShowLabel;
        }

        /// <summary>
        /// Show the current key. Called by the panel when the value changed elsewhere: by a swap
        /// with another row or by the "defaults" button.
        /// </summary>
        public void Refresh()
        {
            if (_keyLabel == null) return;
            if (_listening) return;

            _keyLabel.text = _binding != null ? KeyName(_binding.Value) : "-";
        }

        /// <summary>Stop listening for a key and restore the button's idle look.</summary>
        public void StopListening()
        {
            if (!_listening) return;

            _listening = false;

            if (_buttonBackground != null)
                _buttonBackground.color = _idleColor;

            Refresh();
        }

        private void OnClicked()
        {
            if (_binding == null || _service == null) return;

            _listening = true;
            _listenFrame = Time.frameCount;

            if (_buttonBackground != null)
                _buttonBackground.color = RuntimeUi.Listening;

            if (_keyLabel != null)
                _keyLabel.text = _listeningText;

            // The button stays selected, and Space on it would act as another click,
            // so clear the selection while listening.
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);

            ListenRequested?.Invoke(this);
        }

        private void Update()
        {
            if (!_listening) return;
            if (Time.frameCount == _listenFrame) return;

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                StopListening();
                return;
            }

            foreach (KeyControl control in keyboard.allKeys)
            {
                if (control == null || !control.wasPressedThisFrame) continue;

                Key key = control.keyCode;
                if (key == Key.None) continue;

                StopListening();

                Rebind(_service, _binding, key);

                // A swap may have changed another row; the panel refreshes it,
                // since it is subscribed to service changes.
                Refresh();
                return;
            }
        }

        /// <summary>
        /// Assign a key. If another action already uses it, the two actions swap keys, so after
        /// any press the set stays complete and without duplicates — no silently lost or dead keys.
        /// "Other actions" means every <c>Setting&lt;Key&gt;</c> in every group of the service.
        /// Internal for tests.
        /// </summary>
        internal static void Rebind(ISettingsService service, Setting<Key> binding, Key key)
        {
            if (service == null || binding == null || key == Key.None) return;
            if (binding.Value == key) return;

            var previous = binding.Value;

            foreach (var group in service.Groups)
            {
                foreach (var setting in group.All)
                {
                    if (setting is not Setting<Key> other || other == binding || other.Value != key)
                        continue;

                    service.SetValue(other, previous);
                    service.SetValue(binding, key);
                    return;
                }
            }

            service.SetValue(binding, key);
        }

        private void ShowLabel()
        {
            if (_label == null) return;

            string translated = Loc.IsReady && !string.IsNullOrEmpty(_labelKey)
                ? Loc.Resolve(_labelKey)
                : null;
            _label.text = string.IsNullOrWhiteSpace(translated) || translated == _labelKey
                ? _fallbackLabel
                : translated;
        }

        /// <summary>
        /// Key name as printed on the player's keyboard: layouts differ, and "Q" may be a
        /// different letter on a non-Latin layout.
        ///
        /// If the window font cannot render that name, the Input System key name is shown instead:
        /// it is Latin, always renders and matches the second layout printed on the key itself.
        /// The binding stores the physical key either way, not the letter on it.
        /// </summary>
        private string KeyName(Key key)
        {
            if (key == Key.None) return "-";

            // The Input System name is the same on any layout.
            string fallback = key.ToString().ToUpperInvariant();

            var keyboard = Keyboard.current;
            int index = (int)key - 1;

            string name = null;
            if (keyboard != null && index >= 0 && index < keyboard.allKeys.Count)
                name = keyboard.allKeys[index].displayName;

            if (string.IsNullOrWhiteSpace(name))
                return fallback;

            name = name.ToUpperInvariant();

            return CanRender(name) ? name : fallback;
        }

        /// <summary>
        /// Whether the font can render this name. A static Latin-only font would draw a
        /// non-Latin key name (for example, Cyrillic) as empty boxes or nothing, which is only
        /// noticeable when running the game with that layout.
        ///
        /// Fallback fonts are checked too, and a dynamic font may add the glyph, so once the
        /// project gets a font with the needed characters, real names appear without changes here.
        /// </summary>
        private bool CanRender(string text)
        {
            var font = _keyLabel != null ? _keyLabel.font : null;

            return font != null &&
                   font.HasCharacters(text, out uint[] _, searchFallbacks: true, tryAddCharacter: true);
        }

        private void OnDestroy()
        {
            if (_button != null)
                _button.onClick.RemoveListener(OnClicked);

            Loc.OnLanguageChanged -= ShowLabel;
        }
    }
}
