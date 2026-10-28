using System;
using System.Collections.Generic;
using Spine;
using UnityEngine;

namespace LongLeap.Skins
{
    /// <summary>
    /// Раскладка скинов скелета по категориям кастомизации — то, что хранится
    /// в JSON рядом со скелетом и правится окном <c>LongLeap/Skin Layout Editor</c>.
    ///
    /// Три уровня:
    ///   глобальная категория (голова, торс, ноги) — вкладка UI;
    ///   группа — папка скинов в Spine: скин <c>Hat/Bandana</c> лежит в группе
    ///     <c>Hat</c>. В категорию «голова» складываются группы Hat, Hair, Heads…
    ///   отдельный скин внутри группы — необязательная правка
    ///     (<see cref="SkinOverride"/>): подпись и доступность.
    ///
    /// Сам СПИСОК скинов в JSON не перечисляется: он берётся из скелета по папке
    /// (<see cref="GetSkins"/>), поэтому новый скин в уже разложенной папке
    /// появляется в UI без правки раскладки — правка нужна, только если хочешь
    /// назвать его иначе или пометить Default/Owned From Start.
    /// </summary>
    [Serializable]
    public sealed class SkinLayout
    {
        /// <summary>Скин, который Spine создаёт сам; в кастомизацию не входит.</summary>
        public const string DefaultSkinName = "default";

        public string skeleton;
        public List<Category> categories = new List<Category>();

        [Serializable]
        public sealed class Category
        {
            public string id;
            public string title;
            public List<Group> groups = new List<Group>();
        }

        [Serializable]
        public sealed class Group
        {
            /// <summary>Папка скинов в Spine, посимвольно, с регистром.</summary>
            public string folder;
            public string title;

            /// <summary>
            /// Правки отдельных скинов группы: подпись и доступность. Скин группы,
            /// для которого правки нет, попадает в каталог как есть — Locked, с
            /// именем из Spine вместо подписи.
            /// </summary>
            public List<SkinOverride> skins = new List<SkinOverride>();

            /// <summary>
            /// Зоны перекраса (<c>SpineAppearanceConfig.ColorZone.Name</c>), которые красит
            /// кнопка «color» этой группы. Совпадения с именем группы может и не
            /// быть — зоны заводит художник по смыслу (скин головы красит зону
            /// «Skin», общую для головы и тела), а не по одной на группу каталога.
            /// Пусто — кнопки color у группы не будет.
            /// </summary>
            public List<string> colorZones = new List<string>();

            /// <summary>
            /// Можно ли снять последний вариант группы, оставив её пустой — повторным
            /// нажатием на уже надетый вариант (это решает сервис надетого в игре).
            /// Выключи для групп вроде базовой головы или доски: там всегда должен быть
            /// выбран ровно один вариант, а не «никакой». Аксессуары (шапка, украшение)
            /// обычно снимаемы — там оставляй включённым.
            /// </summary>
            public bool canBeOff = true;

            /// <summary>
            /// Можно ли надеть сразу несколько вариантов группы (аксессуары: цепь,
            /// наушники, напульсник), а не «новый вытесняет прежний». Нажатие на
            /// ненадетый вариант добавляет его к надетым, на надетый — снимает только
            /// его (это решает сервис надетого в игре). Варианты такой группы должны
            /// сидеть в разных слотах Spine: в одном слоте виден только один аттачмент.
            /// </summary>
            public bool multiSelect;
        }

        /// <summary>
        /// Подпись и доступность одного скина — правка поверх того, что есть в Spine.
        /// Иконки скина здесь нет намеренно: ссылку на спрайт JSON не удержит, она
        /// лежит в <see cref="SkinLayoutSourceSO"/>.
        /// </summary>
        [Serializable]
        public sealed class SkinOverride
        {
            /// <summary>Полное имя скина, как в Spine Editor: «Heads/Human».</summary>
            public string skin;

            /// <summary>Подпись в UI. Пусто — игрок увидит имя скина как есть.</summary>
            public string title;

            public SkinAvailability availability = SkinAvailability.Locked;

            /// <summary>
            /// Красить зоны группы (см. <see cref="Group.colorZones"/>) в этот цвет сразу,
            /// как только игрок выбирает этот скин — см. <see cref="hasDefaultColor"/>.
            /// </summary>
            public Color defaultColor = Color.white;

            /// <summary>
            /// Задан ли цвет по умолчанию. Отдельным полем, а не проверкой на белый:
            /// <see cref="Color"/> не различает «не задано» и «задано белым».
            /// </summary>
            public bool hasDefaultColor;
        }

        public static SkinLayout FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new SkinLayout();

            var layout = JsonUtility.FromJson<SkinLayout>(json) ?? new SkinLayout();
            if (layout.categories == null) layout.categories = new List<Category>();

            foreach (var category in layout.categories)
            {
                if (category.groups == null) category.groups = new List<Group>();

                foreach (var group in category.groups)
                {
                    if (group.skins == null) group.skins = new List<SkinOverride>();
                    if (group.colorZones == null) group.colorZones = new List<string>();
                }
            }

            return layout;
        }

        public string ToJson() => JsonUtility.ToJson(this, true);

        /// <summary>
        /// Папка скина: всё до последнего «/». Скин без папки — сам себе группа,
        /// чтобы его тоже можно было положить в категорию.
        /// </summary>
        public static string GetFolder(string skinName)
        {
            if (string.IsNullOrEmpty(skinName)) return string.Empty;

            int slash = skinName.LastIndexOf('/');
            return slash < 0 ? skinName : skinName.Substring(0, slash);
        }

        /// <summary>Все группы скелета с их скинами, в порядке скинов в Spine.</summary>
        public static Dictionary<string, List<string>> CollectGroups(SkeletonData data)
        {
            var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            if (data == null) return groups;

            foreach (var skin in data.Skins)
            {
                if (skin == null || skin.Name == DefaultSkinName) continue;

                var folder = GetFolder(skin.Name);
                if (!groups.TryGetValue(folder, out var skins))
                    groups.Add(folder, skins = new List<string>());

                skins.Add(skin.Name);
            }

            return groups;
        }

        /// <summary>Скины одной группы — для построения UI в рантайме.</summary>
        public static List<string> GetSkins(SkeletonData data, string folder)
        {
            return CollectGroups(data).TryGetValue(folder ?? string.Empty, out var skins)
                ? skins
                : new List<string>();
        }

        /// <summary>
        /// Собрать рантайм-каталог (<see cref="SkinCatalog"/>) по этой раскладке и
        /// реальным скинам скелета. Мусор в раскладке (папки, которых больше нет в
        /// скелете, дубли, категории без единого скина) не роняет загрузку —
        /// пропускаем и пишем в консоль, как и раньше делал <c>SkinCatalogSO</c>.
        /// </summary>
        /// <param name="data">Скелет, чьи папки перечислены в раскладке.</param>
        /// <param name="contextName">Имя для сообщений в консоль — обычно имя ассета-источника.</param>
        public SkinCatalog BuildCatalog(SkeletonData data, string contextName = null)
        {
            var catalog = new SkinCatalog();
            var context = string.IsNullOrEmpty(contextName) ? "SkinLayout" : contextName;

            if (data == null)
            {
                Debug.LogWarning($"[Player] {context}: нет данных скелета — каталог скинов будет пустым.");
                return catalog;
            }

            var groups = CollectGroups(data);

            foreach (var category in categories)
            {
                if (string.IsNullOrWhiteSpace(category.id))
                {
                    Debug.LogWarning($"[Player] {context}: категория без id — пропущена.");
                    continue;
                }

                var categoryId = new SkinCategoryId(category.id);
                var added = AddCategorySkins(catalog, context, categoryId, category, groups);

                if (added == 0)
                {
                    Debug.LogWarning(
                        $"[Player] {context}: в категории '{categoryId}' нет скинов — вкладка будет пустой.");
                    continue;
                }

                catalog.SetCategoryTitle(categoryId, category.title);

                foreach (var group in category.groups)
                {
                    catalog.SetGroupColorZones(group.folder, group.colorZones);
                    catalog.SetGroupCanBeOff(group.folder, group.canBeOff);
                    catalog.SetGroupMultiSelect(group.folder, group.multiSelect);
                }

                if (catalog.GetDefault(categoryId).IsNone)
                    Debug.LogError(
                        $"[Player] {context}: в категории '{categoryId}' нет ни одного открытого с начала " +
                        "скина — пометь скин Default или Owned From Start в Skin Layout Editor.");
            }

            return catalog;
        }

        private static int AddCategorySkins(
            SkinCatalog catalog,
            string context,
            SkinCategoryId categoryId,
            Category category,
            Dictionary<string, List<string>> groups)
        {
            var added = 0;
            var defaults = 0;

            foreach (var group in category.groups)
            {
                if (!groups.TryGetValue(group.folder ?? string.Empty, out var skinNames))
                {
                    Debug.LogWarning(
                        $"[Player] {context}: в категории '{categoryId}' папки '{group.folder}' нет в " +
                        "скелете — пропущена.");
                    continue;
                }

                foreach (var skinName in skinNames)
                {
                    SkinId skin;
                    try
                    {
                        skin = new SkinId(skinName);
                    }
                    catch (ArgumentException e)
                    {
                        Debug.LogWarning($"[Player] {context}: скин '{skinName}' пропущен: {e.Message}");
                        continue;
                    }

                    var over = FindOverride(group, skinName);
                    var title = over != null && !string.IsNullOrWhiteSpace(over.title) ? over.title : null;
                    var availability = over?.availability ?? SkinAvailability.Locked;

                    // Отключённый скин в каталог не входит: Add его только запомнит как
                    // отключённый. Не в общий if ниже — там false значит «дубль».
                    if (availability == SkinAvailability.Disabled)
                    {
                        catalog.Add(categoryId, skin, title, availability);
                        continue;
                    }

                    if (!catalog.Add(categoryId, skin, title, availability))
                    {
                        Debug.LogWarning(
                            $"[Player] {context}: скин '{skin}' уже есть в каталоге — второе вхождение " +
                            $"(категория '{categoryId}') пропущено.");
                        continue;
                    }

                    added++;

                    if (over != null && over.hasDefaultColor)
                        catalog.SetDefaultColor(skin, over.defaultColor);

                    if (availability != SkinAvailability.Default) continue;

                    defaults++;

                    if (defaults > 1)
                        Debug.LogWarning(
                            $"[Player] {context}: в категории '{categoryId}' несколько скинов помечены " +
                            $"Default. Дефолтным остался первый ('{catalog.GetDefault(categoryId)}').");
                }
            }

            return added;
        }

        /// <summary>Правка скина в группе, если её задали в Skin Layout Editor.</summary>
        public static SkinOverride FindOverride(Group group, string skinName)
        {
            if (group?.skins == null) return null;

            foreach (var over in group.skins)
            {
                if (over != null && over.skin == skinName) return over;
            }

            return null;
        }
    }
}
