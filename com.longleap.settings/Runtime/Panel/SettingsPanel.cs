using System;
using System.Collections.Generic;
using LongLeap.Tools;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LongLeap.Settings.UI
{
    /// <summary>
    /// Панель настроек, собранная кодом внутрь заданного <c>root</c>: затемнение,
    /// панель, секции со строками, кнопки «по умолчанию» и «назад». Не привязана
    /// к оконной системе: окно игры (в Darkslide — наследник <c>MenuPageWindow</c>)
    /// только создаёт панель в своём Root и зовёт её в нужные моменты.
    ///
    /// Что показывать, решает <see cref="SettingsWindowConfig"/> — секции, строки,
    /// подписи, шрифт и размер панели. Конфиг берётся из набора по глобальному профилю
    /// устройства (<see cref="DeviceInfo.Profile"/>): ПК, мобильный, веб на ПК, веб на мобильном.
    /// Строка в конфиге ссылается на настройку по ключу, а вид строки зависит
    /// от типа настройки: число — ползунок (<see cref="SettingsVolumeRow"/>),
    /// bool — переключатель (<see cref="SettingsToggleRow"/>), клавиша — кнопка
    /// переназначения (<see cref="SettingsKeyRow"/>).
    ///
    /// Настроек может не быть (<see cref="Bind"/> ещё не позвали) — тогда панель
    /// честно говорит, что настройки недоступны: кнопка возврата работает в любом
    /// случае. Строка с неизвестным ключом — ошибка в консоли, строка пропускается,
    /// остальное собирается как обычно.
    ///
    /// Запись в хранилище панель не делает: ползунок дёргается десятки раз, и
    /// момент записи — закрытие окна — знает только окно.
    /// </summary>
    public sealed class SettingsPanel : IDisposable
    {
        /// <summary>Секция: заголовок, колонка со строками и подсказка.</summary>
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
        /// Собрать панель внутри <paramref name="root"/>. Строки появятся, когда
        /// придут настройки (<see cref="Bind"/>), — раньше показывать нечего.
        /// </summary>
        /// <param name="root">Корень, который растянут на окно: в него кладутся затемнение и панель.</param>
        /// <param name="configs">Конфиги по профилям устройства; хватит одного заполненного.</param>
        /// <param name="onBack">Нажали «назад».</param>
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
        // ПОДКЛЮЧЕНИЕ
        // =========================================================

        /// <summary>
        /// Отдать панели настройки. Зовёт загрузка, когда UI уже поднят: сборка UI
        /// не знает про контейнер сервисов и сама его не достанет.
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
        /// Показать значения, которые поменялись мимо панели: кнопкой mute в
        /// главном меню или чтением сейва. Окно зовёт при открытии.
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

        /// <summary>Перестать ждать клавишу во всех строках. Окно зовёт на закрытии.</summary>
        public void StopListening() => StopListening(null);

        public void Dispose()
        {
            if (_service != null)
                _service.OnChanged -= OnSettingChanged;

            _service = null;
        }

        // =========================================================
        // КОНФИГ И ШРИФТ
        // =========================================================

        /// <summary>
        /// Конфиг под устройство: по глобальному профилю (<see cref="DeviceInfo.Profile"/>).
        /// Нет конфига для профиля — берём ближайший заполненный
        /// (<see cref="SettingsWindowConfigSet.Resolve"/>), нет ни одного — панель
        /// остаётся рабочей: заголовок, сообщение и кнопка возврата, иначе из
        /// меню нельзя было бы выйти назад.
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
                        $"[Settings] в окне настроек не назначен конфиг для профиля {profile}: " +
                        $"взят конфиг для {used}.", _root);
                }

                return config;
            }

            Debug.LogError(
                "[Settings] в окне настроек не назначен ни один конфиг: секций не будет. " +
                "Проставь SettingsWindowConfig хотя бы в одном поле набора конфигов окна.", _root);

            return ScriptableObject.CreateInstance<SettingsWindowConfig>();
        }

        /// <summary>
        /// Шрифт панели. Поле в конфиге — основной путь, но ссылку могли не
        /// проставить, а TMP без шрифта роняет раскладку, если в проекте нет
        /// стандартного шрифта (TMP Essentials не импортированы). Поэтому запасной
        /// вариант — шрифт любого другого текста UI: он точно есть и точно
        /// тот, которым написано остальное меню.
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
                "[Settings] шрифт не найден: подписи в окне настроек будут пустыми. " +
                "Проставь шрифт в поле Font в SettingsWindowConfig.", _root);

            return null;
        }

        // =========================================================
        // СБОРКА
        // =========================================================

        /// <summary>
        /// Постоянная часть: затемнение, панель, заголовки секций и кнопки.
        /// Строится сразу, до того как придут настройки, иначе окно, открытое
        /// без них, было бы пустым прямоугольником. Строки секций добавятся позже,
        /// в <see cref="BuildRows"/>, в уже готовые колонки.
        /// </summary>
        private void BuildChrome()
        {
            var backdrop = SettingsUi.CreateImage("Backdrop", _root, SettingsUi.Backdrop, raycastTarget: true);
            backdrop.rectTransform.Stretch();

            var panel = SettingsUi.CreateImage("Panel", _root, SettingsUi.PanelBackground, raycastTarget: true);
            var panelRect = panel.rectTransform;
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = _config.PanelSize;

            SettingsUi.AddColumn(panelRect, spacing: 12f, padding: new RectOffset(28, 28, 24, 24));

            var title = SettingsUi.CreateText(
                "Title", panelRect, _font, _config.Title, 46, SettingsUi.Accent, TextAlignmentOptions.Center);
            SettingsUi.SetSize(title.gameObject, preferredHeight: 56f);

            _unavailable = SettingsUi.CreateText(
                "Unavailable", panelRect, _font, _config.UnavailableText, SettingsUi.LabelFontSize,
                SettingsUi.TextDim, TextAlignmentOptions.Center);
            SettingsUi.SetSize(_unavailable.gameObject, preferredHeight: 40f);

            // На таче клавиш нет: секции, помеченные HideOnTouch, не создаём.
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
                var hint = SettingsUi.CreateText(
                    "Hint", panel, _font, section.Hint, 18, SettingsUi.TextDim, TextAlignmentOptions.Center);
                SettingsUi.SetSize(hint.gameObject, preferredHeight: 28f);
                view.Hint = hint.gameObject;
            }

            return view;
        }

        private void BuildFooter(RectTransform panel)
        {
            var footer = SettingsUi.CreateRect("Footer", panel);
            var layout = SettingsUi.AddRow(footer, spacing: 16f);
            layout.childAlignment = TextAnchor.MiddleCenter;
            SettingsUi.SetSize(footer.gameObject, preferredHeight: 54f);

            var reset = SettingsUi.CreateButton(
                "Reset", footer, _font, _config.ResetText, new Color(1f, 1f, 1f, 0.10f), out _);
            SettingsUi.SetSize(reset.gameObject, preferredWidth: 220f, preferredHeight: 48f);
            reset.onClick.AddListener(OnReset);

            var back = SettingsUi.CreateButton(
                "Back", footer, _font, _config.BackText, SettingsUi.Accent, out _);
            SettingsUi.SetSize(back.gameObject, preferredWidth: 220f, preferredHeight: 48f);
            back.onClick.AddListener(() => _onBack?.Invoke());
        }

        private GameObject CreateSectionHeader(RectTransform panel, string caption)
        {
            var header = SettingsUi.CreateText("Section", panel, _font, caption, 26, SettingsUi.Accent);
            SettingsUi.SetSize(header.gameObject, preferredHeight: 32f);

            return header.gameObject;
        }

        private static RectTransform CreateColumn(RectTransform panel, string name)
        {
            var column = SettingsUi.CreateRect(name, panel);
            SettingsUi.AddColumn(column, SettingsUi.RowSpacing);
            SettingsUi.SetSize(column.gameObject, preferredHeight: 0f);

            return column;
        }

        /// <summary>
        /// Строки появляются вместе с настройками: раньше показывать нечего.
        /// Проходим по секциям конфига; строку с неизвестным ключом или
        /// незнакомым типом настройки пропускаем с ошибкой, остальные не страдают.
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

                // Секция без единой рабочей строки — пустой заголовок: прячем.
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

        /// <summary>Создать строку для настройки из конфига. false — строки не вышло.</summary>
        private bool AddRow(SectionView section, SettingsWindowConfig.Row row)
        {
            var setting = _service.FindSetting(row.SettingKey);

            if (setting == null)
            {
                Debug.LogError(
                    $"[Settings] в конфиге окна настроек (секция '{section.Config.Title}') " +
                    $"нет настройки с ключом '{row.SettingKey}': строка пропущена. " +
                    "Выбери ключ из списка в SettingsWindowConfig.", _root);

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
                        $"[Settings] для настройки '{setting.Key}' (тип {setting.ValueType.Name}) " +
                        "в окне нет строки: строка пропущена.", _root);
                    return false;
            }
        }

        /// <summary>
        /// Колонка занимает ровно столько, сколько нужно её строкам: панель
        /// фиксированной высоты, и колонка без своего размера схлопнулась бы.
        /// </summary>
        private static void FitColumn(RectTransform column, int rows)
        {
            if (column == null) return;

            float height = rows > 0
                ? rows * SettingsUi.RowHeight + (rows - 1) * SettingsUi.RowSpacing
                : 0f;

            SettingsUi.SetSize(column.gameObject, preferredHeight: height);
        }

        private void ShowUnavailable(bool show)
        {
            if (_unavailable != null)
                _unavailable.gameObject.SetActive(show);
        }

        // =========================================================
        // РЕАКЦИИ
        // =========================================================

        /// <summary>Ждать клавишу может только одна строка, остальные гасим.</summary>
        private void StopListening(SettingsKeyRow except)
        {
            foreach (var row in _keyRows)
            {
                if (row == null || row == except) continue;

                row.StopListening();
            }
        }

        /// <summary>
        /// Значение поменялось в сервисе. Обновляем все строки, а не одну:
        /// переназначение клавиши меняет ещё и ту строку, у которой её забрали.
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
