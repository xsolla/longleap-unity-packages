using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LongLeap.UI
{
    /// <summary>
    /// Настройки: громкости.
    ///
    /// Окно собирает себя кодом — в префабе у него только пустой Root. Так
    /// строка настройки появляется одной строкой здесь, а не сборкой иерархии
    /// руками, и окно не расходится с тем, какие группы настроек есть в игре.
    /// Кирпичи сборки — <see cref="SettingsUi"/>, строки — <see cref="SettingsVolumeRow"/>.
    ///
    /// Что показывать, решает игра: сервис настроек и строки громкостей ей приносит
    /// загрузка (<see cref="Bind"/>). Настроек может не быть — тогда окно открывается
    /// и честно говорит, что настройки недоступны: кнопка возврата работает
    /// в любом случае.
    ///
    /// Громкость слышно сразу, пока тянут ползунок; в хранилище всё уезжает
    /// на закрытии окна — одной записью, а не на каждое движение.
    /// </summary>
    public sealed class SettingsWindow : MenuPageWindow
    {
        public System.Func<Task> ReturnToPause { get; set; }

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
        // =========================================================
        // ВИД
        // =========================================================

        [Header("Вид")]

        [Tooltip("Шрифт окна. Пусто — TMP подставит стандартный.")]
        [SerializeField] private TMP_FontAsset _font;

        [Tooltip("Размер панели настроек в пикселях опорного разрешения 1080x1920.")]
        [SerializeField] private Vector2 _panelSize = new Vector2(900f, 640f);

        // =========================================================
        // ПОДПИСИ
        // =========================================================

        [Header("Подписи")]

        [SerializeField] private string _title = "SETTINGS";

        [SerializeField] private string _soundSection = "SOUND";


        [SerializeField] private string _resetText = "DEFAULTS";

        [SerializeField] private string _backText = "BACK";

        [Tooltip("Что показать, если настройки не поднялись при загрузке.")]
        [SerializeField] private string _unavailableText = "Settings are unavailable in this session.";

        // =========================================================
        // СОСТОЯНИЕ
        // =========================================================

        private ISettingsService _service;
        private IReadOnlyList<VolumeRow> _volumes;
        private Action _resetToDefaults;
        private Func<Task> _save;

        private RectTransform _soundColumn;
        private TextMeshProUGUI _unavailable;

        private readonly List<SettingsVolumeRow> _volumeRows = new List<SettingsVolumeRow>();

        private bool _rowsBuilt;

        // =========================================================
        // ПОДКЛЮЧЕНИЕ
        // =========================================================

        /// <summary>
        /// Отдать окну настройки игры. Зовёт загрузка, когда UI уже поднят: пакет UI
        /// не знает про контейнер сервисов и сама их не достанет.
        /// </summary>
        /// <param name="service">Сервис настроек: строки пишут в него и слушают его.</param>
        /// <param name="volumes">Строки громкостей в порядке показа.</param>
        /// <param name="resetToDefaults">Кнопка «по умолчанию». Null — кнопка ничего не делает.</param>
        /// <param name="save">Сохранение на закрытии окна. Null — не сохраняем.</param>
        public void Bind(ISettingsService service, IReadOnlyList<VolumeRow> volumes,
            Action resetToDefaults = null, Func<Task> save = null)
        {
            if (_service != null)
                _service.OnChanged -= OnSettingChanged;

            _service = service;
            _volumes = volumes;
            _resetToDefaults = resetToDefaults;
            _save = save;

            if (_service == null) return;

            _service.OnChanged += OnSettingChanged;

            BuildRows();
        }

        /// <summary>Строка громкости: настройка и подпись (подпись — запасная, если нет перевода).</summary>
        public sealed class VolumeRow
        {
            public VolumeRow(NumericSetting<float> setting, string label)
            {
                Setting = setting ?? throw new ArgumentNullException(nameof(setting));
                Label = label;
            }

            public NumericSetting<float> Setting { get; }

            public string Label { get; }
        }

        // =========================================================
        // WINDOW
        // =========================================================

        public override Task OpenAsync()
        {
            // Значения могли поменяться мимо окна: кнопкой mute в главном меню
            // или чтением сейва.
            RefreshRows();

            return base.OpenAsync();
        }

        /// <summary>
        /// Закрытие — момент записи: пока окно открыто, ползунок дёргается
        /// десятки раз, и писать в хранилище на каждое движение незачем.
        /// </summary>
        public override async Task CloseAsync()
        {
            await base.CloseAsync();

            if (_save != null)
                Forget(_save());
        }

        protected override void Awake()
        {
            base.Awake();

            _font = ResolveFont();

            BuildChrome();
        }

        /// <summary>
        /// Шрифт окна. Поле в префабе — основной путь, но ссылку могли не
        /// проставить, а TMP без шрифта роняет раскладку: стандартного шрифта
        /// в проекте нет (TMP Essentials не импортированы). Поэтому запасной
        /// вариант — шрифт любого другого текста UI: он точно есть и точно
        /// тот, которым написано остальное меню.
        /// </summary>
        private TMP_FontAsset ResolveFont()
        {
            if (_font != null) return _font;

            if (TMP_Settings.defaultFontAsset != null)
                return TMP_Settings.defaultFontAsset;

            var canvas = GetComponentInParent<Canvas>();

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
                "Проставь шрифт в поле Font на окне SettingsWindow в префабе UI.", this);

            return null;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            if (_service != null)
                _service.OnChanged -= OnSettingChanged;
        }

        // =========================================================
        // СБОРКА
        // =========================================================

        /// <summary>
        /// Постоянная часть окна: затемнение, панель, заголовки секций и кнопки.
        /// Строится в Awake, до того как придут настройки, иначе окно, открытое
        /// без них, было бы пустым прямоугольником.
        /// </summary>
        private void BuildChrome()
        {
            var root = (RectTransform)Root.transform;

            var backdrop = SettingsUi.CreateImage("Backdrop", root, SettingsUi.Backdrop, raycastTarget: true);
            backdrop.rectTransform.Stretch();

            var panel = SettingsUi.CreateImage("Panel", root, SettingsUi.PanelBackground, raycastTarget: true);
            var panelRect = panel.rectTransform;
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = _panelSize;

            SettingsUi.AddColumn(panelRect, spacing: 12f, padding: new RectOffset(28, 28, 24, 24));

            var title = SettingsUi.CreateText(
                "Title", panelRect, _font, _title, 46, SettingsUi.Accent, TextAlignmentOptions.Center);
            SettingsUi.SetSize(title.gameObject, preferredHeight: 56f);

            _unavailable = SettingsUi.CreateText(
                "Unavailable", panelRect, _font, _unavailableText, SettingsUi.LabelFontSize,
                SettingsUi.TextDim, TextAlignmentOptions.Center);
            SettingsUi.SetSize(_unavailable.gameObject, preferredHeight: 40f);

            CreateSectionHeader(panelRect, _soundSection);
            _soundColumn = CreateColumn(panelRect, "SoundRows");

            BuildFooter(panelRect);

            ShowUnavailable(true);
        }

        private void BuildFooter(RectTransform panel)
        {
            var footer = SettingsUi.CreateRect("Footer", panel);
            var layout = SettingsUi.AddRow(footer, spacing: 16f);
            layout.childAlignment = TextAnchor.MiddleCenter;
            SettingsUi.SetSize(footer.gameObject, preferredHeight: 54f);

            var reset = SettingsUi.CreateButton(
                "Reset", footer, _font, _resetText, new Color(1f, 1f, 1f, 0.10f), out _);
            SettingsUi.SetSize(reset.gameObject, preferredWidth: 220f, preferredHeight: 48f);
            reset.onClick.AddListener(OnReset);

            var back = SettingsUi.CreateButton(
                "Back", footer, _font, _backText, SettingsUi.Accent, out _);
            SettingsUi.SetSize(back.gameObject, preferredWidth: 220f, preferredHeight: 48f);
            back.onClick.AddListener(OnBackClicked);
        }

        private void CreateSectionHeader(RectTransform panel, string caption)
        {
            var header = SettingsUi.CreateText("Section", panel, _font, caption, 26, SettingsUi.Accent);
            SettingsUi.SetSize(header.gameObject, preferredHeight: 32f);
        }

        private RectTransform CreateColumn(RectTransform panel, string name)
        {
            var column = SettingsUi.CreateRect(name, panel);
            SettingsUi.AddColumn(column, SettingsUi.RowSpacing);
            SettingsUi.SetSize(column.gameObject, preferredHeight: 0f);

            return column;
        }

        /// <summary>Строки появляются вместе с настройками: раньше показывать нечего.</summary>
        private void BuildRows()
        {
            if (_rowsBuilt || _service == null) return;

            _rowsBuilt = true;

            if (_volumes != null)
                foreach (var volume in _volumes)
                    if (volume != null) AddVolumeRow(volume.Setting, volume.Label);

            FitColumn(_soundColumn, _volumeRows.Count);

            ShowUnavailable(false);
            RefreshRows();
        }

        private void AddVolumeRow(NumericSetting<float> setting, string fallbackLabel)
        {
            var row = SettingsVolumeRow.Create(_soundColumn, _font, fallbackLabel);
            row.Bind(_service, setting);

            _volumeRows.Add(row);
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
        /// <summary>
        /// Значение поменялось в сервисе. Обновляем все строки, а не одну:
        /// переназначение клавиши меняет ещё и ту строку, у которой её забрали.
        /// </summary>
        private void OnSettingChanged(Setting setting) => RefreshRows();

        private void RefreshRows()
        {
            foreach (var row in _volumeRows)
                if (row != null) row.Refresh();
        }

        private void OnReset()
        {
            if (_resetToDefaults == null) return;

            _resetToDefaults();

            RefreshRows();
        }

        private void OnBackClicked() => Forget(BackAsync());
    }
}
