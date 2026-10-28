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
    /// Строка управления: подпись действия и кнопка с текущей клавишей.
    ///
    /// Нажали кнопку — строка ждёт клавишу и подсвечивается. Следующая нажатая
    /// клавиша становится новой; Esc отменяет. Занятая клавиша не теряется:
    /// действия меняются ею местами (см. <see cref="Rebind"/>),
    /// поэтому «пустых» действий после переназначения не бывает.
    ///
    /// Ждать может только одна строка: начиная ждать, строка говорит об этом
    /// событием <see cref="ListenRequested"/>, и окно гасит остальные.
    /// </summary>
    public sealed class SettingsKeyRow : MonoBehaviour
    {
        /// <summary>Строка начала ждать клавишу — остальным пора перестать.</summary>
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
        /// Кадр, в котором начали ждать: нажатие, которое открыло ожидание
        /// (Enter или пробел на самой кнопке), не должно тут же стать новой
        /// клавишей.
        /// </summary>
        private int _listenFrame;

        private Color _idleColor;

        /// <summary>Собрать строку внутри колонки окна.</summary>
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

        /// <summary>Привязать строку к клавише действия.</summary>
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
        /// Показать текущую клавишу. Зовёт окно, когда значение поменялось не
        /// здесь: обменом с другой строкой или кнопкой «по умолчанию».
        /// </summary>
        public void Refresh()
        {
            if (_keyLabel == null) return;
            if (_listening) return;

            _keyLabel.text = _binding != null ? KeyName(_binding.Value) : "-";
        }

        /// <summary>Перестать ждать клавишу и вернуть кнопке обычный вид.</summary>
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

            // Кнопка остаётся выбранной, и пробел на ней сработал бы как
            // повторное нажатие — снимаем выделение, пока ждём клавишу.
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

                // Обмен мог задеть соседнюю строку — обновить её должно окно,
                // оно подписано на изменения сервиса.
                Refresh();
                return;
            }
        }

        /// <summary>
        /// Назначить клавишу. Если она уже занята другим действием, действия
        /// меняются клавишами: так после любого нажатия набор остаётся полным и
        /// без дублей — молча потерянных или «мёртвых» кнопок не появляется.
        /// «Другие действия» — это все настройки-клавиши всех групп сервиса.
        /// </summary>
        private static void Rebind(ISettingsService service, Setting<Key> binding, Key key)
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
        /// Имя клавиши так, как она подписана на клавиатуре игрока: раскладка
        /// у всех своя, и «Q» на ней может быть «Й».
        ///
        /// Если шрифт окна такую подпись не нарисует, показываем имя клавиши
        /// из Input System: оно латинское, всегда рисуется и совпадает со
        /// второй раскладкой, напечатанной на самой клавише. Привязка всё
        /// равно хранит физическую клавишу, а не букву на ней.
        /// </summary>
        private string KeyName(Key key)
        {
            if (key == Key.None) return "-";

            // Имя из Input System — то же самое на любой раскладке.
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
        /// Шрифт умеет нарисовать эту подпись. Шрифт меню статический и знает
        /// только латиницу: кириллическая подпись с русской раскладки вышла бы
        /// пустой строкой на экране, а не «неправильной буквой», — заметить
        /// такое можно только запустив игру с этой раскладкой.
        ///
        /// Запасные шрифты тоже учитываем, а динамическому разрешаем добрать
        /// глиф: если в проект однажды добавят шрифт с кириллицей, подписи
        /// станут настоящими сами, без правок здесь.
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
