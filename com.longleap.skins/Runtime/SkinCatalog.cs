using System;
using System.Collections.Generic;
using UnityEngine;

namespace LongLeap.Skins
{
    /// <summary>
    /// Каталог скинов в памяти. Заполняется один раз при загрузке — обычно из
    /// раскладки JSON и скелета (<c>SkinLayoutSourceSO</c>, пакет
    /// com.longleap.skins.spine), но <see cref="Add"/> публичный намеренно:
    /// тесты и редакторные инструменты собирают каталог двумя строчками, без
    /// ассетов и без Unity.
    ///
    /// Порядок и категорий, и скинов внутри категории — порядок добавления,
    /// то есть порядок в ассете: именно в нём UI рисует вкладки и варианты,
    /// и переставить их должно быть можно перетаскиванием в инспекторе.
    /// </summary>
    public sealed class SkinCatalog : ISkinCatalog
    {
        private static readonly SkinId[] NoSkins = new SkinId[0];
        private static readonly string[] NoZones = new string[0];

        private readonly List<SkinCategoryId> _categories = new List<SkinCategoryId>();
        private readonly Dictionary<SkinCategoryId, List<SkinId>> _skinsByCategory =
            new Dictionary<SkinCategoryId, List<SkinId>>();
        private readonly Dictionary<SkinId, SkinCategoryId> _categoryBySkin =
            new Dictionary<SkinId, SkinCategoryId>();
        private readonly Dictionary<SkinId, string> _titles = new Dictionary<SkinId, string>();
        private readonly Dictionary<SkinCategoryId, string> _categoryTitles =
            new Dictionary<SkinCategoryId, string>();
        private readonly HashSet<SkinId> _ownedFromStart = new HashSet<SkinId>();
        private readonly HashSet<SkinId> _disabled = new HashSet<SkinId>();
        private readonly Dictionary<SkinId, Color> _defaultColorBySkin = new Dictionary<SkinId, Color>();
        private readonly Dictionary<SkinId, Sprite> _iconBySkin = new Dictionary<SkinId, Sprite>();

        // Резерв категории: явно помеченный дефолтный скин и, на случай если его
        // не пометили, первый открытый с начала. Оба — первые встреченные:
        // каталог читается сверху вниз, и «дефолтный» должен значить один и тот
        // же скин при каждой загрузке.
        private readonly Dictionary<SkinCategoryId, SkinId> _defaultByCategory =
            new Dictionary<SkinCategoryId, SkinId>();
        private readonly Dictionary<SkinCategoryId, SkinId> _firstFromStartByCategory =
            new Dictionary<SkinCategoryId, SkinId>();

        /// <summary>
        /// Стартовый набор по категориям — скины <see cref="SkinAvailability.OwnedFromStart"/>
        /// в порядке каталога. Какие из них надеть (по одному на группу или все) решает
        /// сервис надетого: правила групп — его забота, а не каталога.
        /// </summary>
        private readonly Dictionary<SkinCategoryId, List<SkinId>> _startingOutfitByCategory =
            new Dictionary<SkinCategoryId, List<SkinId>>();

        /// <summary>
        /// Зоны перекраса (см. SpineAppearanceConfig.ColorZone) по папке группы — из
        /// раскладки, ключ тот же «folder», что и у Group. По нему панель
        /// кастомизации решает, какая кнопка «color» есть у группы и что она красит.
        /// </summary>
        private readonly Dictionary<string, IReadOnlyList<string>> _colorZonesByGroup =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        /// <summary>
        /// Группы, у которых нельзя снять последний вариант — здесь только исключения
        /// из умолчания «можно», иначе пришлось бы перечислять и снимаемые группы.
        /// </summary>
        private readonly HashSet<string> _groupsThatCannotBeOff = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Группы, где можно надеть несколько вариантов сразу, — тоже только исключения.</summary>
        private readonly HashSet<string> _multiSelectGroups = new HashSet<string>(StringComparer.Ordinal);

        public IReadOnlyList<SkinCategoryId> Categories => _categories;

        /// <summary>
        /// Добавить скин в категорию; категория заводится на первом своём скине.
        ///
        /// Скин может лежать только в одной категории — в спайне имена скинов
        /// уникальны на скелет, и один скин в двух вкладках означал бы, что
        /// каталог заполнен с ошибкой. Возвращает false, если скин уже был
        /// добавлен: разбираться с этим (предупредить, показать, где) — дело
        /// вызывающего, он знает, откуда взялись данные.
        ///
        /// Второй <see cref="SkinAvailability.Default"/> в категории не заменяет
        /// первый: дефолтный скин один, и менять его местами при каждой правке
        /// ассета нельзя — иначе персонаж новичка будет выглядеть по-разному в
        /// соседних билдах. Такой скин просто останется открытым с начала.
        ///
        /// <see cref="SkinAvailability.Disabled"/> в каталог не добавляется — скин лишь
        /// запоминается как отключённый (см. <see cref="IsDisabled"/>), и ответ тоже false.
        /// </summary>
        public bool Add(
            SkinCategoryId category,
            SkinId skin,
            string title = null,
            SkinAvailability availability = SkinAvailability.Locked)
        {
            if (category.IsNone || skin.IsNone) return false;
            if (_categoryBySkin.ContainsKey(skin)) return false;

            if (availability == SkinAvailability.Disabled)
            {
                _disabled.Add(skin);
                return false;
            }

            if (!_skinsByCategory.TryGetValue(category, out var skins))
            {
                skins = new List<SkinId>();
                _skinsByCategory.Add(category, skins);
                _categories.Add(category);
            }

            skins.Add(skin);
            _categoryBySkin.Add(skin, category);

            if (!string.IsNullOrWhiteSpace(title))
                _titles.Add(skin, title.Trim());

            if (availability == SkinAvailability.Locked) return true;

            _ownedFromStart.Add(skin);

            if (!_firstFromStartByCategory.ContainsKey(category))
                _firstFromStartByCategory.Add(category, skin);

            if (availability == SkinAvailability.OwnedFromStart)
            {
                if (!_startingOutfitByCategory.TryGetValue(category, out var starting))
                    _startingOutfitByCategory.Add(category, starting = new List<SkinId>());

                starting.Add(skin);
            }

            if (availability == SkinAvailability.Default && !_defaultByCategory.ContainsKey(category))
                _defaultByCategory.Add(category, skin);

            return true;
        }

        public IReadOnlyList<SkinId> GetSkins(SkinCategoryId category)
        {
            if (!category.IsNone && _skinsByCategory.TryGetValue(category, out var skins))
                return skins;

            return NoSkins;
        }

        public IReadOnlyList<SkinId> GetStartingOutfit(SkinCategoryId category)
        {
            if (!category.IsNone && _startingOutfitByCategory.TryGetValue(category, out var skins))
                return skins;

            return NoSkins;
        }

        /// <summary>
        /// Задать заголовок категории. Отдельным методом, а не параметром
        /// <see cref="Add"/>: заголовок у категории один, а скинов в ней много, и
        /// повторять его на каждом скине значило бы спорить самому с собой.
        /// </summary>
        public void SetCategoryTitle(SkinCategoryId category, string title)
        {
            if (category.IsNone || string.IsNullOrWhiteSpace(title)) return;

            _categoryTitles[category] = title.Trim();
        }

        /// <summary>
        /// Заголовок категории. Не задан — id заглавными: «head» → «HEAD». Строка
        /// категории в UI создаётся по каталогу, и показать над ней пустоту хуже,
        /// чем показать сам id.
        /// </summary>
        public string GetCategoryTitle(SkinCategoryId category)
        {
            if (category.IsNone) return "";

            return _categoryTitles.TryGetValue(category, out var title)
                ? title
                : category.Value.ToUpperInvariant();
        }

        public string GetTitle(SkinId skin)
        {
            if (skin.IsNone) return "";

            return _titles.TryGetValue(skin, out var title) ? title : skin.Value;
        }

        /// <summary>
        /// Дефолтный скин категории. Если его не пометили — первый открытый с
        /// начала: подставить хоть что-то честнее, чем оставить категорию пустой
        /// из-за незаполненной галки в ассете.
        /// </summary>
        public SkinId GetDefault(SkinCategoryId category)
        {
            if (category.IsNone) return default;

            if (_defaultByCategory.TryGetValue(category, out var skin))
                return skin;

            return _firstFromStartByCategory.TryGetValue(category, out var fallback) ? fallback : default;
        }

        public bool Contains(SkinId skin) => !skin.IsNone && _categoryBySkin.ContainsKey(skin);

        public bool TryGetCategory(SkinId skin, out SkinCategoryId category)
        {
            if (!skin.IsNone && _categoryBySkin.TryGetValue(skin, out category))
                return true;

            category = default;
            return false;
        }

        public bool IsOwnedFromStart(SkinId skin) => !skin.IsNone && _ownedFromStart.Contains(skin);

        public bool IsDisabled(SkinId skin) => !skin.IsNone && _disabled.Contains(skin);

        /// <summary>
        /// Отключённые скины — для инструментов, которым надо отличить «отключили
        /// намеренно» от «пропал из скелета» (например, иконки в SkinLayoutSourceSO).
        /// </summary>
        public IReadOnlyCollection<SkinId> DisabledSkins => _disabled;

        /// <summary>Задать цвет по умолчанию для скина — красится сразу, как только его выбирают.</summary>
        public void SetDefaultColor(SkinId skin, Color color)
        {
            if (skin.IsNone) return;

            _defaultColorBySkin[skin] = color;
        }

        public bool TryGetDefaultColor(SkinId skin, out Color color)
        {
            if (!skin.IsNone && _defaultColorBySkin.TryGetValue(skin, out color))
                return true;

            color = default;
            return false;
        }

        /// <summary>
        /// Задать иконку скина. Отдельным методом, а не параметром <see cref="Add"/>:
        /// иконки приходят не из JSON-раскладки, а из ассета-источника (ссылку на
        /// спрайт текстовый JSON не удержит), и докладываются уже в собранный каталог.
        /// null — как и не звать вовсе.
        /// </summary>
        public void SetIcon(SkinId skin, Sprite icon)
        {
            if (skin.IsNone || icon == null) return;

            _iconBySkin[skin] = icon;
        }

        public Sprite GetIcon(SkinId skin)
        {
            if (skin.IsNone) return null;

            return _iconBySkin.TryGetValue(skin, out var icon) ? icon : null;
        }

        /// <summary>
        /// Задать зоны перекраса для группы (папки скинов). Пустой список — как и
        /// не звать вовсе: у группы просто не будет кнопки color.
        /// </summary>
        public void SetGroupColorZones(string group, IReadOnlyList<string> zones)
        {
            if (string.IsNullOrEmpty(group) || zones == null || zones.Count == 0) return;

            _colorZonesByGroup[group] = zones;
        }

        public IReadOnlyList<string> GetGroupColorZones(string group)
        {
            if (string.IsNullOrEmpty(group)) return NoZones;

            return _colorZonesByGroup.TryGetValue(group, out var zones) ? zones : NoZones;
        }

        /// <summary>Задать, можно ли у группы не быть надетым вариантом вовсе. По умолчанию — можно.</summary>
        public void SetGroupCanBeOff(string group, bool canBeOff)
        {
            if (string.IsNullOrEmpty(group)) return;

            if (canBeOff) _groupsThatCannotBeOff.Remove(group);
            else _groupsThatCannotBeOff.Add(group);
        }

        public bool CanGroupBeOff(string group) =>
            string.IsNullOrEmpty(group) || !_groupsThatCannotBeOff.Contains(group);

        /// <summary>Задать, можно ли надеть несколько вариантов группы сразу. По умолчанию — нельзя.</summary>
        public void SetGroupMultiSelect(string group, bool multiSelect)
        {
            if (string.IsNullOrEmpty(group)) return;

            if (multiSelect) _multiSelectGroups.Add(group);
            else _multiSelectGroups.Remove(group);
        }

        public bool IsGroupMultiSelect(string group) =>
            !string.IsNullOrEmpty(group) && _multiSelectGroups.Contains(group);
    }
}
