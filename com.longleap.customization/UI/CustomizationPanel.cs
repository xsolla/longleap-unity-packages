using System;
using System.Collections.Generic;
using LongLeap.Skins;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace LongLeap.Customization
{
    /// <summary>
    /// Кастомизация: скины доски и персонажа.
    ///
    /// Не окно, а панель внутри главного меню хоста: меню остаётся на экране,
    /// поэтому панель не регистрируется как отдельное окно и своего префаба не
    /// имеет — она часть префаба меню. Меню знает про панель ровно три вещи:
    /// «покажись», «спрячься» и «я закрылась» (<see cref="Closed"/> — по нему меню
    /// возвращает свою обычную панель), всё остальное здесь.
    ///
    /// Персонаж на панели — один спайновый скелет, и он же превью: игрок сразу видит
    /// выбранное на самом персонаже, а у вариантов — только иконки скинов (Skin Layout
    /// Editor; приходят из инвентаря, как подписи и цвета). Панель одевает
    /// его через <see cref="ISkinTarget"/> и про спайн не знает ничего — ни типов,
    /// ни рантайма. Скелет обычно лежит не внутри панели, а рядом, в префабе меню:
    /// тогда его отдаёт меню — <see cref="BindCharacter"/>.
    ///
    /// ГЛОБАЛЬНУЮ КАТЕГОРИЮ (HEAD, TORSO, LEGS, BOARD) ВЫБИРАЮТ ВНЕШНИЕ ТОГГЛЫ —
    /// не панель их создаёт, они уже лежат в префабе, обычно в одной ToggleGroup.
    /// Категорию тоггла панель узнаёт по ИМЕНИ ЕГО ОБЪЕКТА: объект «HEAD» — это
    /// категория «head» (регистр не важен, см. <see cref="SkinCategoryId"/>).
    ///
    /// Выбранная категория показывается СТРОКАМИ ПО ГРУППАМ: скины категории
    /// приходят из инвентаря уже сгруппированными (каждая группа — соседний
    /// отрезок списка, порядок как в каталоге), и панель режет список по смене
    /// группы (см. <see cref="GroupOf"/>) — на каждую группу одна строка
    /// (<see cref="SkinGroupRow"/>, «Hair», «Hat»…) в <c>Category_Root</c>
    /// (<see cref="UnityEngine.UI.VerticalLayoutGroup"/>, строки друг под другом), а
    /// внутри строки — её варианты (<see cref="SkinVariantView"/>) в ряд
    /// (<see cref="UnityEngine.UI.HorizontalLayoutGroup"/> на самой строке).
    /// Переключили тоггл — старые строки и варианты снесены, созданы новые.
    /// Нажатие на вариант сразу переодевает персонажа — панель лишь просит об
    /// этом <see cref="IEquipmentService"/>.
    ///
    /// СОДЕРЖИМОЕ РЯДА СОБИРАЕТСЯ ИЗ КАТАЛОГА, А НЕ РУКАМИ В ПРЕФАБЕ: добавили скин
    /// в ассет каталога — он сам появится в ряду, когда откроют его категорию.
    ///
    /// СОСТОЯНИЯ ПАНЕЛЬ НЕ ХРАНИТ — ни содержимого, ни выбора:
    /// <list type="bullet">
    /// <item>какие есть категории и скины и что из них открыто — знает инвентарь
    /// (<see cref="IInventoryService"/>);</item>
    /// <item>что надето — знает <see cref="IEquipmentService"/>, он же это сохраняет,
    /// поэтому выбор игрока переживает выход из игры.</item>
    /// </list>
    ///
    /// Закрытые скины показываются с замком и не нажимаются — игрок видит, что в
    /// игре есть ещё скины, но выбрать может только свои.
    ///
    /// С какой группой игрок работает сейчас (нажал её вариант или кнопку color),
    /// панель сообщает персонажу через <see cref="IPreviewPose"/>, если он это умеет:
    /// для некоторых групп ему надо встать иначе — грип, например, виден только на
    /// перевёрнутой доске. Смена категории и закрытие панели — «ни с какой».
    ///
    /// Пример иерархии в префабе меню хоста (компонент — на всегда включённом
    /// объекте, гаснет только содержимое):
    /// <code>
    /// HostMenu
    /// └── Root
    ///     ├── Menu                     (обычная панель меню: кнопки, билд, музыка)
    ///     ├── Character                (контейнер: его двигает панель и масштабирует ZoomTransition)
    ///     │   ├── shadow               (тень — ПЕРВОЙ, чтобы рисоваться под персонажем)
    ///     │   └── Visual               (скелет персонажа + ISkinTarget)
    ///     └── Customization            (компонент панели, всегда включён)
    ///         └── Root                 (содержимое панели, включается на открытии)
    ///             ├── UpPanel          (тоггл-категории: HEAD, TORSO, LEGS, BOARD — ToggleGroup)
    ///             └── DownMenu
    ///                 ├── BackGround
    ///                 │   └── Category_Root   (VerticalLayoutGroup: сюда кладутся строки групп)
    ///                 └── palete          (ColorPaletteView, спрятана по умолчанию —
    ///                                       кнопка color строки открывает её под зоны
    ///                                       группы из Color Zone Mappings)
    /// </code>
    /// </summary>
    public sealed class CustomizationPanel : MonoBehaviour
    {
        // =========================================================
        // ПАНЕЛЬ
        // =========================================================

        [Header("Панель")]

        [Tooltip("Содержимое панели: включается при открытии, гаснет при закрытии.")]
        [SerializeField] private GameObject _root;

        [Tooltip("Кнопка «назад»: прячет панель, меню под ней никуда не уходило.")]
        [SerializeField] private Button _closeButton;

        [SerializeField] private Graphic _menuLogo;
        [SerializeField] private Vector2 _previewPosition = new Vector2(270f, -432f);
        private RectTransform _previewRect;
        private Vector2 _menuCharacterPosition;
        private bool _previewPositionApplied;

        // =========================================================
        // ПЕРСОНАЖ
        // =========================================================

        [Header("Персонаж")]

        [Tooltip("Главный объект спайна. На нём (или внутри) висит компонент, умеющий " +
                 "надевать скины — реализация ISkinTarget. Персонажа можно отдать и из " +
                 "кода — меню (BindCharacter) или загрузка (Bind): тогда поле оставь пустым.")]
        [SerializeField] private GameObject _character;

        // =========================================================
        // КАТЕГОРИИ
        // =========================================================

        [Header("Категории")]

        [Tooltip("Внешние переключатели глобальных категорий (HEAD, TORSO, LEGS, BOARD) — " +
                 "обычно один Toggle на категорию в общей ToggleGroup. Категорию тоггла " +
                 "панель берёт из имени его объекта: «HEAD» → категория «head».")]
        [SerializeField] private Toggle[] _categoryToggles;

        [Tooltip("Подпись открытой категории: HEAD, TORSO, LEGS, BOARD. Необязательна.")]
        [SerializeField] private TMP_Text _categoryLabel;

        // =========================================================
        // ВАРИАНТЫ
        // =========================================================

        [Header("Варианты")]

        [Tooltip("Шаблон строки группы (Hair, Hat…): панель размножает его по группам " +
                 "открытой категории и в каждую копию кладёт варианты этой группы. " +
                 "Держи выключенным в сцене — включённый останется на экране лишней строкой.")]
        [SerializeField] private SkinGroupRow _groupRowTemplate;

        [Tooltip("Куда панель кладёт строки групп открытой категории (Category_Root). " +
                 "Пусто — берётся родитель шаблона; для шаблона-префаба задать обязательно.")]
        [SerializeField] private Transform _groupRowsRoot;

        [Tooltip("Шаблон кнопки варианта: панель размножает его по скинам каждой " +
                 "группы. Держи его выключенным в сцене — включённый останется на " +
                 "экране лишним вариантом.")]
        [SerializeField] private SkinVariantView _variantTemplate;

        // =========================================================
        // ПАЛИТРА
        // =========================================================

        [Header("Палитра")]

        [Tooltip("Круглая палитра перекраса: кнопка «color» строки группы открывает её " +
                 "под цветовые зоны этой группы (см. Skin Layout Editor — Color " +
                 "Zones). Пусто — кнопки color ничего не делают.")]
        [SerializeField] private ColorPaletteView _palette;

        /// <summary>
        /// Кнопка color группы, под которую сейчас открыта палитра — палитра красит
        /// зоны сама, а эту кнопку тонируем в тот же цвет мы, по <see cref="ColorPaletteView.ColorChanged"/>.
        /// </summary>
        private Button _activeColorButton;

        /// <summary>
        /// Зоны, которые красит открытая сейчас палитра — те же, что отданы ей в
        /// <see cref="OpenPalette"/>. Нужны, чтобы сохранить цвет через
        /// <see cref="_colors"/>: палитра знает только про <see cref="IColorTarget"/>
        /// и ничего не сохраняет сама.
        /// </summary>
        private IReadOnlyList<string> _activeZones;

        /// <summary>
        /// Вещь, которую красит открытая палитра, — только у группы с множественным
        /// выбором (см. <see cref="ColorTargetOf"/>). <c>IsNone</c> — палитра красит
        /// зоны целиком, как у обычной группы.
        /// </summary>
        private SkinId _activeSkin;

        /// <summary>Появляется, когда персонажа отдали; до этого нажатия менять нечего.</summary>
        private ISkinTarget _skins;

        /// <summary>
        /// Тот же персонаж, что и <see cref="_skins"/>, но со стороны перекраса зон —
        /// появляется сам, когда реализация ISkinTarget умеет и то, и другое.
        /// </summary>
        private IColorTarget _colorTarget;

        /// <summary>
        /// Поза того же персонажа. Необязательна: без неё персонаж просто не
        /// поворачивается под выбранную группу. Ищется в <see cref="BindCharacter"/>.
        /// </summary>
        private IPreviewPose _pose;

        /// <summary>
        /// Что есть в игре и что открыто у игрока. Приносит хост (<see cref="BindPlayer"/>):
        /// панель не знает ни про контейнер сервисов, ни про сейвы.
        /// </summary>
        private IInventoryService _inventory;

        /// <summary>Что надето. Он же и сохраняет выбор.</summary>
        private IEquipmentService _equipment;

        /// <summary>Цвета зон, которые выбрал игрок. Он же и сохраняет выбор.</summary>
        private IColorService _colors;

        /// <summary>Открытая сейчас категория. До первого выбора — <c>IsNone</c>.</summary>
        private SkinCategoryId _currentCategory;

        /// <summary>Скины открытой категории, в порядке каталога — один к одному с кнопками вариантов.</summary>
        private readonly List<SkinId> _currentSkins = new List<SkinId>();

        /// <summary>Кнопки варианта, созданные из шаблона — один к одному с <see cref="_currentSkins"/>.</summary>
        private readonly List<SkinVariantView> _variantViews = new List<SkinVariantView>();

        /// <summary>Строки групп (Hair, Hat…), созданные из шаблона для открытой категории.</summary>
        private readonly List<SkinGroupRow> _groupRows = new List<SkinGroupRow>();

        /// <summary>Набор для <see cref="ISkinTarget.SetSkins"/> — один список на все нажатия.</summary>
        private readonly List<string> _chosen = new List<string>();

        /// <summary>
        /// Слушатели тогглов — храним, чтобы отписаться в OnDestroy: тогглы чужие
        /// (лежат в префабе меню, не панель их создала), и снимать за них подписку
        /// приходится тем же экземпляром делегата, которым подписались.
        /// </summary>
        private UnityAction<bool>[] _toggleListeners;

        private bool _warnedNoPlayer;

        /// <summary>
        /// Содержимое панели. Никогда не null: без проставленной ссылки панель
        /// управляет собственным объектом — работать будет, но вместе с содержимым
        /// будет гаснуть и компонент.
        /// </summary>
        private GameObject Root => _root != null ? _root : gameObject;

        /// <summary>Панель показана прямо сейчас.</summary>
        public bool IsOpen => Root.activeSelf;

        /// <summary>
        /// Панель спряталась — своей кнопкой «назад» или из кода. Слушает меню
        /// (хост-меню): пока панель на экране, обычная панель меню выключена, и
        /// включить её обратно некому, кроме этого события.
        /// </summary>
        public event Action Closed;

        // =========================================================
        // ПОКАЗ
        // =========================================================

        /// <summary>Показать панель. Зовёт меню по кнопке Customization.</summary>
        public void Open()
        {
            if (!_previewPositionApplied && _character != null &&
                _character.TryGetComponent(out _previewRect))
            {
                _menuCharacterPosition = _previewRect.anchoredPosition;
                _previewRect.anchoredPosition = _previewPosition;
                _previewPositionApplied = true;
            }
            if (_menuLogo != null) _menuLogo.enabled = false;
            if (_inventory == null && !_warnedNoPlayer)
            {
                _warnedNoPlayer = true;

                Debug.LogWarning(
                    "[Menu] панели кастомизации не отдали игрока — категорий и вариантов не будет. " +
                    "Обычно это значит, что не поднялся сервис игрока или нет ассета каталога скинов.",
                    this);
            }

            // Категорию уже выбрали раньше (тоггл остаётся включённым между открытиями) —
            // достаточно освежить состояние, пересоздавать кнопки незачем. Ни разу не
            // выбирали (первое открытие) — выбираем как по умолчанию задано в тогглах.
            if (_currentCategory.IsNone)
                SelectInitialCategory();
            else
                ShowCurrentCategoryState();

            Root.SetActive(true);
        }

        /// <summary>
        /// Спрятать панель. Зовёт кнопка «назад» и меню, когда открывается: панель —
        /// состояние внутри меню, и оставлять её открытой с прошлого раза не нужно.
        /// </summary>
        public void Close()
        {
            if (_previewPositionApplied)
            {
                if (_previewRect != null) _previewRect.anchoredPosition = _menuCharacterPosition;
                _previewPositionApplied = false;
            }
            if (_menuLogo != null) _menuLogo.enabled = true;
            Root.SetActive(false);

            _palette?.Close();

            // Персонаж виден и на обычной панели меню — с перевёрнутой доской там ему
            // стоять незачем.
            _pose?.Focus(null);

            Closed?.Invoke();
        }

        // =========================================================
        // ПЕРСОНАЖ
        // =========================================================

        /// <summary>
        /// Отдать панели персонажа объектом: на нём или внутри него ищется тот, кто
        /// умеет надевать скины. Зовёт меню — скелет лежит в его префабе рядом с
        /// панелью, а не внутри неё, потому что виден и на обычной панели меню.
        ///
        /// <c>null</c> — персонажа больше нет, нажатия перестают что-либо менять.
        /// </summary>
        public void BindCharacter(GameObject character)
        {
            _character = character;
            _pose = character != null ? character.GetComponentInChildren<IPreviewPose>(true) : null;

            if (character == null)
            {
                Bind(null);
                return;
            }

            var skins = character.GetComponentInChildren<ISkinTarget>(true);

            if (skins == null)
            {
                Debug.LogError(
                    $"[Menu] на персонаже «{character.name}» нет компонента, умеющего " +
                    "надевать скины (реализации ISkinTarget) — кнопки кастомизации будут " +
                    "нажиматься, а персонаж останется как есть.", this);
                return;
            }

            Bind(skins);
        }

        /// <summary>
        /// Отдать панели персонажа напрямую. Нужно, если скелет лежит не в префабе
        /// меню, а его кто-то создаёт — например загрузка. <c>null</c> — персонажа
        /// больше нет.
        ///
        /// Сразу надевает текущий выбор: персонаж мог появиться в позе настройки.
        /// </summary>
        public void Bind(ISkinTarget skins)
        {
            if (_skins == skins) return;

            _skins = skins;
            _colorTarget = skins as IColorTarget;

            // Персонаж сменился (или пропал) — старая палитра красила бы уже не того.
            _palette?.Close();

            ApplyOutfit();
            ApplyColors();
        }

        // =========================================================
        // ИГРОК
        // =========================================================

        /// <summary>
        /// Отдать панели сервисы игрока: из инвентаря берутся категории, скины и замки,
        /// из «надетого» — что показано выбранным, из цветов — что красить. Зовёт хост
        /// один раз; все три <c>null</c> — панель снова пустая. Панель намеренно
        /// принимает сами сервисы, а не агрегат игрока: агрегат — дело игры.
        ///
        /// Панель подписывается на оба сервиса:
        /// <list type="bullet">
        /// <item>инвентарь — скин могли выдать, пока меню открыто (награда за ачивку,
        /// чит из Tools), и замок должен пропасть сам;</item>
        /// <item>надетое — его может сменить не только эта панель (например, оно
        /// починилось после чит-очистки), и отметка «выбрано» обязана это показать.</item>
        /// </list>
        /// </summary>
        public void BindPlayer(IInventoryService inventory, IEquipmentService equipment, IColorService colors)
        {
            if (_inventory == inventory && _equipment == equipment && _colors == colors) return;

            Unsubscribe();

            _inventory = inventory;
            _equipment = equipment;
            _colors = colors;

            if (_inventory != null)
                _inventory.Changed += OnInventoryChanged;

            if (_equipment != null)
                _equipment.Changed += OnEquipmentChanged;

            SelectInitialCategory();

            ApplyOutfit();
            ApplyColors();
        }

        /// <summary>
        /// Состав инвентаря изменился. Скины не появляются и не исчезают из КАТАЛОГА,
        /// поэтому кнопки не пересоздаём — обновляем замки открытой категории. Другие
        /// категории сейчас не показаны, обновлять в них нечего.
        /// </summary>
        private void OnInventoryChanged()
        {
            if (!_currentCategory.IsNone)
                ShowCurrentCategoryState();
        }

        /// <summary>Что-то переодели: если это открытая категория — отметки и подпись, персонаж — всегда.</summary>
        private void OnEquipmentChanged(SkinCategoryId category, SkinId skin)
        {
            if (category == _currentCategory)
                ShowCurrentCategoryState();

            ApplyOutfit();
        }

        /// <summary>
        /// Надеть на персонажа то, что на игроке сейчас.
        ///
        /// Пока игрока не отдали, скелет не трогаем вовсе: пустой набор раздел бы его
        /// до голого тела, хотя художник собрал в инспекторе вид «по умолчанию» —
        /// именно его и надо показывать до загрузки.
        /// </summary>
        private void ApplyOutfit()
        {
            if (_skins == null || _equipment == null) return;

            _chosen.Clear();

            var outfit = _equipment.Outfit;

            for (var i = 0; i < outfit.Count; i++)
                _chosen.Add(outfit[i].Value);

            _skins.SetSkins(_chosen);
        }

        /// <summary>
        /// Покрасить персонажа в цвета, которые сохранил игрок. Зовётся вместе с
        /// <see cref="ApplyOutfit"/> — после смены персонажа и после того, как
        /// игрока отдали панели: своих цветов у панели нет, только у сервиса.
        /// </summary>
        private void ApplyColors()
        {
            if (_colorTarget == null || _colors == null) return;

            foreach (var entry in _colors.Colors)
                _colorTarget.SetZoneColor(entry.Key, entry.Value);

            // Цвета отдельных вещей — после зон: они ложатся поверх цвета зоны.
            foreach (var entry in _colors.SkinColors)
                _colorTarget.SetSkinColor(entry.Key.Value, entry.Value);
        }

        // =========================================================
        // КАТЕГОРИИ (ТОГГЛЫ)
        // =========================================================

        /// <summary>
        /// Выбрать категорию для первого показа: ту, чей тоггл уже включён в префабе
        /// (обычно HEAD), а нет такого — первый тоггл по порядку. Тогглов не задали
        /// вовсе — открывать нечего, и об этом стоит сказать явно: без них ряд
        /// вариантов никогда не наполнится.
        /// </summary>
        private void SelectInitialCategory()
        {
            if (_categoryToggles == null || _categoryToggles.Length == 0)
            {
                if (_inventory != null)
                    Debug.LogError(
                        "[Menu] панели кастомизации не заданы переключатели категорий " +
                        "(Category Toggles) — Category_Root останется пустым.", this);

                return;
            }

            foreach (var toggle in _categoryToggles)
            {
                if (toggle == null || !toggle.isOn) continue;

                SelectCategory(CategoryOf(toggle));
                return;
            }

            var first = _categoryToggles[0];
            if (first != null)
                SelectCategory(CategoryOf(first));
        }

        /// <summary>Тоггл переключили — если включили, а не выключили, показываем его категорию.</summary>
        private void OnToggleChanged(Toggle toggle, bool isOn)
        {
            if (isOn) SelectCategory(CategoryOf(toggle));
        }

        /// <summary>Категория тоггла — его имя в объекте, как есть: «HEAD» → «head».</summary>
        private static SkinCategoryId CategoryOf(Toggle toggle) => new SkinCategoryId(toggle.gameObject.name);

        /// <summary>Показать категорию: снести старый ряд вариантов, создать новый по каталогу.</summary>
        private void SelectCategory(SkinCategoryId category)
        {
            if (category.IsNone) return;

            _currentCategory = category;

            // Группы прошлой категории больше не на экране — и поза под них тоже не нужна.
            _pose?.Focus(null);

            if (_categoryLabel != null)
                _categoryLabel.text = _inventory != null ? _inventory.GetCategoryTitle(category) : category.Value;

            BuildVariants(category);
        }

        // =========================================================
        // ВАРИАНТЫ
        // =========================================================

        private void BuildVariants(SkinCategoryId category)
        {
            DestroyVariants();

            _currentSkins.Clear();

            if (_inventory == null) return;

            var rowTemplate = ResolveRowTemplate();
            var itemTemplate = ResolveVariantTemplate();
            if (rowTemplate == null || itemTemplate == null) return;

            var rowParent = _groupRowsRoot != null ? _groupRowsRoot : rowTemplate.transform.parent;
            var skins = _inventory.GetSkins(category);

            for (var i = 0; i < skins.Count; i++)
                _currentSkins.Add(skins[i]);

            // Инвентарь отдаёт скины уже сгруппированными — все скины одной группы
            // (Hair, Hat…) идут подряд, в порядке каталога (см. SkinLayout.BuildCatalog).
            // Поэтому строку заводим не по явному списку групп, а по смене группы
            // в самом списке скинов — панели не нужно знать про группы отдельно.
            string currentGroup = null;
            SkinGroupRow currentRow = null;

            for (var i = 0; i < _currentSkins.Count; i++)
            {
                var skin = _currentSkins[i];
                var group = GroupOf(skin);

                if (currentRow == null || group != currentGroup)
                {
                    currentRow = Instantiate(rowTemplate, rowParent);
                    currentRow.gameObject.name = string.IsNullOrEmpty(group) ? "Group" : group;
                    currentRow.gameObject.SetActive(true);
                    currentRow.SetTitle(string.IsNullOrEmpty(group) ? string.Empty : group.Replace('_', ' '));

                    currentGroup = group;
                    _groupRows.Add(currentRow);

                    {
                        var zones = _inventory.GetColorZones(group);

                        // У группы без зон в раскладке (Skin Layout Editor — Color Zones)
                        // кнопки color (и её подложки) быть не должно вовсе — иначе игрок жал
                        // бы на неё и не понимал, почему палитра открывается, а цвет никуда
                        // не ложится.
                        currentRow.SetColorAvailable(zones.Count > 0);

                        if (zones.Count > 0 && currentRow.ColorButton != null)
                        {
                            var key = group;
                            var button = currentRow.ColorButton;
                            button.onClick.AddListener(() => OpenPalette(key, zones, button));

                            // Зоны группы красятся палитрой всегда одним и тем же цветом
                            // (см. ColorPaletteView.ApplyColor), поэтому для подсветки кнопки
                            // хватает сохранённого цвета первой зоны — если игрок его уже задавал.
                            // У группы с множественным выбором кнопка показывает цвет вещи,
                            // а не зоны, — это делает ShowCurrentCategoryState ниже.
                            if (!_inventory.IsGroupMultiSelect(group) &&
                                _colors != null && button.targetGraphic != null &&
                                _colors.TryGetColor(zones[0], out var savedColor))
                                button.targetGraphic.color = savedColor;
                        }
                    }
                }

                var view = Instantiate(itemTemplate, currentRow.ItemsRoot);
                view.gameObject.name = skin.Value;
                view.gameObject.SetActive(true);

                // Обработчик не запоминаем: кнопка живёт на созданном нами объекте и
                // умрёт вместе с ним, а с ней и подписка.
                if (view.Button != null)
                    view.Button.onClick.AddListener(() => EquipSkin(skin));

                _variantViews.Add(view);
            }

            if (_currentSkins.Count == 0)
                Debug.LogWarning(
                    $"[Menu] в категории «{category}» нет скинов — Category_Root будет пустым.", this);

            // Строки и варианты только что созданы: VerticalLayoutGroup/ContentSizeFitter
            // в Category_Root и HorizontalLayoutGroup в каждой строке пересчитаются сами,
            // но только на следующий кадр. Без принудительного пересчёта здесь один кадр
            // маска и ScrollRect работали бы по старым размерам — варианты наложились бы
            // друг на друга или обрезались не по границе.
            Canvas.ForceUpdateCanvases();

            ShowCurrentCategoryState();
        }

        /// <summary>
        /// Группа скина — папка Spine, зашитая в его имя («Hair/Long_hair» → «Hair»).
        /// Скин без папки (имя без «/») — сам себе группа: строка получится с одним
        /// вариантом, но не потеряется вовсе.
        /// </summary>
        private static string GroupOf(SkinId skin)
        {
            var value = skin.Value;
            var slash = value.LastIndexOf('/');
            return slash < 0 ? value : value.Substring(0, slash);
        }

        /// <summary>
        /// Шаблон строки группы. В сцене его гасим: включённым он остался бы на
        /// экране лишней строкой. Шаблон-префаб (не на сцене) не трогаем — это
        /// ассет, но тогда должно быть задано, куда класть строки.
        /// </summary>
        private SkinGroupRow ResolveRowTemplate()
        {
            if (_groupRowTemplate == null)
            {
                Debug.LogError(
                    "[Menu] панели кастомизации не задан шаблон строки группы — Category_Root " +
                    "останется пустым. Положи в поле Group Row Template префаб строки или " +
                    "выключенный объект внутри панели.", this);
                return null;
            }

            var inScene = _groupRowTemplate.gameObject.scene.IsValid();

            if (inScene && _groupRowTemplate.gameObject.activeSelf)
                _groupRowTemplate.gameObject.SetActive(false);

            if (!inScene && _groupRowsRoot == null)
            {
                Debug.LogError(
                    "[Menu] шаблон строки группы — префаб, но не задано, куда класть строки " +
                    "(Group Rows Root / Category_Root). Класть их некуда.", this);
                return null;
            }

            return _groupRowTemplate;
        }

        /// <summary>
        /// Шаблон варианта. В сцене его гасим: включённым он остался бы на экране
        /// лишним вариантом. Шаблон-префаб (не на сцене) не трогаем — это ассет, но
        /// тогда должно быть задано, куда класть варианты — в строку группы, а не
        /// сюда, поэтому Variants Root у самого шаблона варианта не проверяем.
        /// </summary>
        private SkinVariantView ResolveVariantTemplate()
        {
            if (_variantTemplate == null)
            {
                Debug.LogError(
                    "[Menu] панели кастомизации не задан шаблон варианта — строки группы " +
                    "останутся пустыми. Положи в поле Variant Template префаб кнопки варианта " +
                    "или выключенный объект внутри панели.", this);
                return null;
            }

            if (_variantTemplate.gameObject.scene.IsValid() && _variantTemplate.gameObject.activeSelf)
                _variantTemplate.gameObject.SetActive(false);

            return _variantTemplate;
        }

        /// <summary>
        /// Сносим строки групп — вместе с ними уходят и их варианты, отдельно
        /// уничтожать <see cref="_variantViews"/> не нужно: они лежат внутри строк.
        /// </summary>
        private void DestroyVariants()
        {
            // Строки со старыми кнопками color сейчас снесём — палитра, открытая под
            // одну из них, осталась бы красить зону, которой уже не с чем управлять.
            _palette?.Close();

            // Кнопка, которую подсвечивала палитра, сейчас будет уничтожена вместе со
            // своей строкой — держать на неё ссылку дальше нельзя.
            _activeColorButton = null;
            _activeZones = null;
            _activeSkin = default;

            foreach (var row in _groupRows)
            {
                if (row != null)
                    Destroy(row.gameObject);
            }

            _groupRows.Clear();
            _variantViews.Clear();
        }

        /// <summary>Отметки вариантов и замки открытой категории — под то, что надето сейчас.</summary>
        private void ShowCurrentCategoryState()
        {
            var count = Mathf.Min(_variantViews.Count, _currentSkins.Count);

            for (var i = 0; i < count; i++)
            {
                var skin = _currentSkins[i];
                var icon = _inventory != null ? _inventory.GetIcon(skin) : null;

                _variantViews[i].Show(skin.Value, IsOwned(skin),
                    _equipment != null && _equipment.IsEquipped(skin), icon);
            }

            RefreshMultiSelectColorButtons();
        }

        /// <summary>
        /// Кнопка color группы с множественным выбором красит последнюю надетую вещь
        /// группы (см. <see cref="ColorTargetOf"/>), а та меняется с каждым нажатием на
        /// вариант. Поэтому после каждого переодевания: кнопка видна, только пока есть
        /// что красить, и подсвечена цветом именно этой вещи; палитра, открытая под
        /// прежнюю вещь, закрывается — иначе она продолжила бы красить не ту.
        /// </summary>
        private void RefreshMultiSelectColorButtons()
        {
            if (_inventory == null) return;

            foreach (var row in _groupRows)
            {
                if (row == null) continue;

                var group = row.gameObject.name;
                if (!_inventory.IsGroupMultiSelect(group)) continue;

                var zones = _inventory.GetColorZones(group);
                var target = ColorTargetOf(group);

                row.SetColorAvailable(zones.Count > 0 && !target.IsNone);

                if (_palette != null && _palette.IsOpenFor(group) && target != _activeSkin)
                    _palette.Close();

                if (target.IsNone || row.ColorButton == null || row.ColorButton.targetGraphic == null)
                    continue;

                row.ColorButton.targetGraphic.color = CurrentColorOf(target, zones);
            }
        }

        /// <summary>
        /// Какой цвет сейчас на вещи: свой, если игрок его задавал, иначе цвет зоны, иначе
        /// нейтральный цвет кнопки из шаблона строки — кнопку красит каждая вещь группы
        /// по очереди, и «ничего не задано» не должно показывать цвет предыдущей.
        /// </summary>
        private Color CurrentColorOf(SkinId skin, IReadOnlyList<string> zones)
        {
            if (_colors != null && _colors.TryGetSkinColor(skin, out var skinColor))
                return skinColor;

            if (_colors != null && zones.Count > 0 && _colors.TryGetColor(zones[0], out var zoneColor))
                return zoneColor;

            var neutral = _groupRowTemplate != null && _groupRowTemplate.ColorButton != null
                ? _groupRowTemplate.ColorButton.targetGraphic
                : null;

            return neutral != null ? neutral.color : Color.white;
        }

        /// <summary>
        /// Что красит кнопка color группы с множественным выбором — последняя надетая
        /// вещь группы. «Последняя» берётся из порядка надетого: сервис дописывает новый
        /// скин в конец и в том же порядке его сохраняет, поэтому отдельно помнить
        /// последнее нажатие не нужно. Ничего не надето — <c>IsNone</c>.
        /// </summary>
        private SkinId ColorTargetOf(string group)
        {
            if (_equipment == null) return default;

            var outfit = _equipment.Outfit;

            for (var i = outfit.Count - 1; i >= 0; i--)
            {
                if (GroupOf(outfit[i]) == group)
                    return outfit[i];
            }

            return default;
        }

        // =========================================================
        // ВЫБОР
        // =========================================================

        /// <summary>
        /// Выбрать скин или снять его повторным нажатием. Решение «можно ли» принимает
        /// <see cref="IEquipmentService"/> — он один знает, что у игрока открыто,
        /// какие варианты конфликтуют, и он же сохраняет результат.
        ///
        /// Экран обновится не здесь, а по событию сервиса: так панель показывает одно
        /// и то же и когда переодел игрок, и когда надетое сменилось само.
        /// </summary>
        private void EquipSkin(SkinId skin)
        {
            // Позу — до решения сервиса: игрок смотрит на эту группу, даже если
            // повторное нажатие ничего не сняло (у группы без «выключено»).
            _pose?.Focus(GroupOf(skin));

            if (_equipment == null) return;

            if (_equipment.IsEquipped(skin))
            {
                _equipment.Unequip(skin);
                return;
            }

            _equipment.Equip(skin);
            ApplyDefaultColor(skin);
        }

        /// <summary>
        /// Если у скина задан цвет по умолчанию (Skin Layout Editor), красим им зоны
        /// его группы сразу при выборе — тем самым цветом, что и ручная перекраска
        /// (см. <see cref="OnPaletteColorChanged"/>), и точно так же он переживёт
        /// смену скина внутри группы: следующий скин без своего цвета зону не тронет.
        ///
        /// В группе с множественным выбором цвет ложится на саму вещь, а не на общую
        /// зону — иначе надетые раньше аксессуары перекрасились бы вместе с новым.
        ///
        /// Куда именно лечь цвету, решает <see cref="SkinDefaultColors"/> — то же правило,
        /// которым загрузка красит стартовый набор нового игрока; здесь только переносим
        /// записанное на персонажа целиком (<see cref="ApplyColors"/>), чтобы не повторять
        /// это правило второй раз.
        /// </summary>
        private void ApplyDefaultColor(SkinId skin)
        {
            if (!SkinDefaultColors.TryApply(_inventory, _colors, skin)) return;

            ApplyColors();

            if (_inventory.TryGetDefaultColor(skin, out var color))
                UpdateColorButtonTint(GroupOf(skin), color);
        }

        /// <summary>
        /// Кнопка color строки красится в тот же цвет, что и зоны — как и после ручной
        /// перекраски палитрой (см. <see cref="OnPaletteColorChanged"/>), иначе кнопка
        /// показывала бы цвет прошлого скина группы до следующего открытия категории.
        /// </summary>
        private void UpdateColorButtonTint(string group, Color color)
        {
            foreach (var row in _groupRows)
            {
                if (row == null || row.gameObject.name != group) continue;
                if (row.ColorButton != null && row.ColorButton.targetGraphic != null)
                    row.ColorButton.targetGraphic.color = color;
                return;
            }
        }

        private bool IsOwned(SkinId skin) => _inventory != null && _inventory.Has(skin);

        // =========================================================
        // ПАЛИТРА
        // =========================================================

        /// <summary>
        /// Открыть палитру под зоны группы. Повторное нажатие той же кнопки color —
        /// не «перекрасить в тот же цвет заново», а спрятать палитру: она уже открыта
        /// под эту группу.
        ///
        /// У группы с множественным выбором палитра красит не зоны целиком, а одну
        /// вещь — последнюю надетую (<see cref="ColorTargetOf"/>): зона у аксессуаров
        /// общая, и цвет зоны перекрасил бы их все разом.
        /// </summary>
        private void OpenPalette(string group, IReadOnlyList<string> zones, Button colorButton)
        {
            // Красят эту группу — значит, на неё и смотрят: персонаж встаёт так, чтобы
            // перекрашиваемое было видно (грип — на перевёрнутой доске).
            _pose?.Focus(group);

            if (_palette == null) return;

            if (_palette.IsOpenFor(group))
            {
                _palette.Close();
                return;
            }

            if (_colorTarget == null) return;

            var skin = default(SkinId);
            var target = _colorTarget;

            if (_inventory != null && _inventory.IsGroupMultiSelect(group))
            {
                skin = ColorTargetOf(group);
                if (skin.IsNone) return;

                target = new SkinColorTarget(_colorTarget, skin.Value);
            }

            // Запоминаем кнопку, зоны и вещь ДО открытия: цвет может прилететь от палитры
            // уже на первом кадре (если она восстанавливает прошлое положение ручки).
            _activeColorButton = colorButton;
            _activeZones = zones;
            _activeSkin = skin;

            _palette.Open(group, zones, target);
        }

        /// <summary>
        /// Палитра умеет красить только зоны (<see cref="IColorTarget.SetZoneColor"/>) и
        /// про вещи не знает. Для группы с множественным выбором её цвет переадресуется
        /// одной вещи — так палитру не пришлось учить второму режиму.
        /// </summary>
        private sealed class SkinColorTarget : IColorTarget
        {
            private readonly IColorTarget _target;
            private readonly string _skin;

            public SkinColorTarget(IColorTarget target, string skin)
            {
                _target = target;
                _skin = skin;
            }

            public bool SetZoneColor(string zone, Color color) => _target.SetSkinColor(_skin, color);

            public bool SetSkinColor(string skin, Color color) => _target.SetSkinColor(skin, color);
        }

        /// <summary>
        /// Палитра покрасила зоны — тонируем кнопку color той же группы тем же
        /// цветом и сохраняем выбор: сама палитра ничего не сохраняет, она знает
        /// только про <see cref="IColorTarget"/>.
        /// </summary>
        private void OnPaletteColorChanged(Color color)
        {
            if (_activeColorButton != null && _activeColorButton.targetGraphic != null)
                _activeColorButton.targetGraphic.color = color;

            if (_colors == null) return;

            if (!_activeSkin.IsNone)
            {
                _colors.SetSkinColor(_activeSkin, color);
                return;
            }

            if (_activeZones == null) return;

            for (var i = 0; i < _activeZones.Count; i++)
                _colors.SetColor(_activeZones[i], color);
        }

        // =========================================================
        // UNITY
        // =========================================================

        private void Awake()
        {
            if (_root == null)
                Debug.LogError(
                    "[Menu] у панели кастомизации не задан Root — гаснуть будет сам объект панели.",
                    this);

            // Компонент панели включён всегда, поэтому подписаться хватает одного раза.
            if (_closeButton != null)
                _closeButton.onClick.AddListener(Close);

            if (_palette != null)
                _palette.ColorChanged += OnPaletteColorChanged;

            SubscribeToggles();

            // Персонажа могли положить в саму панель. Лежит он рядом, в префабе меню, —
            // его отдаст меню через BindCharacter, и поле остаётся пустым.
            if (_character != null)
                BindCharacter(_character);
        }

        private void OnDestroy()
        {
            if (_closeButton != null)
                _closeButton.onClick.RemoveListener(Close);

            if (_palette != null)
                _palette.ColorChanged -= OnPaletteColorChanged;

            UnsubscribeToggles();

            // Не через BindPlayer(null) и Bind(null): те пересобрали бы варианты и
            // полезли бы к персонажу, а его объект в этот момент уже может быть снесён.
            // Нам здесь нужна ровно отписка.
            Unsubscribe();

            _inventory = null;
            _equipment = null;
            _colors = null;
            _skins = null;
            _pose = null;
        }

        private void SubscribeToggles()
        {
            if (_categoryToggles == null) return;

            _toggleListeners = new UnityAction<bool>[_categoryToggles.Length];

            for (var i = 0; i < _categoryToggles.Length; i++)
            {
                var toggle = _categoryToggles[i];
                if (toggle == null) continue;

                UnityAction<bool> listener = isOn => OnToggleChanged(toggle, isOn);
                _toggleListeners[i] = listener;
                toggle.onValueChanged.AddListener(listener);
            }
        }

        private void UnsubscribeToggles()
        {
            if (_categoryToggles == null || _toggleListeners == null) return;

            for (var i = 0; i < _categoryToggles.Length; i++)
            {
                if (_categoryToggles[i] != null && _toggleListeners[i] != null)
                    _categoryToggles[i].onValueChanged.RemoveListener(_toggleListeners[i]);
            }

            _toggleListeners = null;
        }

        private void Unsubscribe()
        {
            if (_inventory != null)
                _inventory.Changed -= OnInventoryChanged;

            if (_equipment != null)
                _equipment.Changed -= OnEquipmentChanged;
        }
    }
}
