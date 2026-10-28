#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spine.Unity;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace LongLeap.Skins.EditorTools
{
    /// <summary>
    /// Раскладка скинов Spine-скелета по категориям кастомизации.
    ///
    /// В скелете десятки папок скинов (Hat, Hair, Heads, Boots…), а в UI нужно
    /// несколько вкладок. Здесь создаются глобальные категории («голова»,
    /// «ноги») и в них перетаскиваются папки-группы; результат сохраняется в
    /// JSON (<see cref="SkinLayout"/>), по которому потом строится окно
    /// кастомизации.
    ///
    /// Папка может лежать только в одной категории: непристроенные показаны
    /// справа, чтобы было видно, что ещё не разложено.
    ///
    /// Иконки скинов правятся здесь же, в строке скина, но пишутся не в JSON, а в
    /// <see cref="SkinLayoutSourceSO"/>, который ссылается на открытый JSON (см.
    /// <see cref="FindIconSource"/>): ссылку на спрайт текстовый JSON не удержит.
    /// </summary>
    internal sealed class SkinLayoutEditorWindow : EditorWindow
    {
        private const string MenuPath = "LongLeap/Skin Layout Editor";
        private const string SkeletonPrefKey = "LongLeap.SkinLayout.Skeleton";
        private const string JsonPrefKey = "LongLeap.SkinLayout.Json";
        private const string AppearanceConfigPrefKey = "LongLeap.SkinLayout.AppearanceConfig";
        private const string JsonSuffix = "_skin_layout";

        private const float CategoriesWidth = 240f;
        private const float UnassignedWidth = 300f;
        private const float AppearanceConfigWidth = 320f;
        private const float SkinRowHeight = 64f;

        [SerializeField] private SkeletonDataAsset _skeleton;
        [SerializeField] private TextAsset _json;
        [SerializeField] private SpineAppearanceConfig _appearanceConfig;

        /// <summary>Куда пишутся иконки скинов — источник каталога для открытого JSON. Может не быть.</summary>
        private SkinLayoutSourceSO _iconSource;

        private Vector2 _appearanceSkinsScroll;
        private Vector2 _appearanceDetailsScroll;

        private SkinLayout _layout;
        private Dictionary<string, List<string>> _groups = new Dictionary<string, List<string>>();
        private List<string> _slotNames = new List<string>();
        private int _selected = -1;

        private ReorderableList _categoryList;
        private ReorderableList _groupList;

        private readonly HashSet<string> _expanded = new HashSet<string>();
        private readonly HashSet<string> _expandedGroups = new HashSet<string>();
        private string _search = string.Empty;
        private Vector2 _categoriesScroll;
        private Vector2 _groupsScroll;
        private Vector2 _unassignedScroll;

        [MenuItem(MenuPath, false, 2100)]
        private static void Open()
        {
            var window = GetWindow<SkinLayoutEditorWindow>();
            window.titleContent = new GUIContent("Skin Layout");
            window.minSize = new Vector2(820f + AppearanceConfigWidth, 400f);
            window.Show();
        }

        private void OnEnable()
        {
            saveChangesMessage = "В раскладке скинов есть несохранённые изменения. Сохранить?";

            if (_skeleton == null) _skeleton = LoadByGuid<SkeletonDataAsset>(SkeletonPrefKey);
            if (_json == null) _json = LoadByGuid<TextAsset>(JsonPrefKey);
            if (_appearanceConfig == null) _appearanceConfig = LoadByGuid<SpineAppearanceConfig>(AppearanceConfigPrefKey);

            Reload();
        }

        /// <summary>
        /// Источник иконок могли создать или перенацелить на этот JSON, пока окно
        /// было открыто, — ищем заново, когда к окну возвращаются.
        /// </summary>
        private void OnFocus()
        {
            _iconSource = FindIconSource(_json);
        }

        public override void SaveChanges()
        {
            Save();
            base.SaveChanges();
        }

        #region Loading

        private void Reload()
        {
            _groups = LoadGroups(_skeleton);
            _slotNames = LoadSlotNames(_skeleton);
            _layout = _json != null ? SkinLayout.FromJson(_json.text) : null;
            _iconSource = FindIconSource(_json);
            _selected = _layout != null && _layout.categories.Count > 0 ? 0 : -1;
            hasUnsavedChanges = false;

            BuildCategoryList();
            BuildGroupList();
        }

        private static Dictionary<string, List<string>> LoadGroups(SkeletonDataAsset asset)
        {
            if (asset == null) return new Dictionary<string, List<string>>();

            var data = asset.GetSkeletonData(true);
            if (data == null)
                Debug.LogWarning($"[SkinLayout] не удалось прочитать скелет '{asset.name}'.", asset);

            return SkinLayout.CollectGroups(data);
        }

        /// <summary>Имена всех слотов скелета — для меню Hide Rule (см. <see cref="ShowHideSlotsMenu"/>).</summary>
        private static List<string> LoadSlotNames(SkeletonDataAsset asset)
        {
            var names = new List<string>();
            if (asset == null) return names;

            var data = asset.GetSkeletonData(true);
            if (data == null) return names;

            foreach (var slot in data.Slots)
                names.Add(slot.Name);

            return names;
        }

        /// <summary>
        /// <see cref="SkinLayoutSourceSO"/>, чей Layout JSON — открытый здесь файл: его
        /// читает игра, значит, иконки для этой раскладки должны лежать в нём. Ищется,
        /// а не назначается полем: так иконки нельзя записать в источник другой
        /// раскладки. Таких несколько — берём первый; нет ни одного — иконки задать
        /// негде, поле в строке скина выключено.
        /// </summary>
        private static SkinLayoutSourceSO FindIconSource(TextAsset json)
        {
            if (json == null) return null;

            foreach (var guid in AssetDatabase.FindAssets($"t:{nameof(SkinLayoutSourceSO)}"))
            {
                var source = AssetDatabase.LoadAssetAtPath<SkinLayoutSourceSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (source != null && source.Layout == json) return source;
            }

            return null;
        }

        private static T LoadByGuid<T>(string prefKey) where T : UnityEngine.Object
        {
            var guid = EditorPrefs.GetString(prefKey, string.Empty);
            if (string.IsNullOrEmpty(guid)) return null;

            var path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<T>(path);
        }

        private static void StoreGuid(string prefKey, UnityEngine.Object asset)
        {
            if (asset == null)
            {
                EditorPrefs.DeleteKey(prefKey);
                return;
            }

            EditorPrefs.SetString(prefKey, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset)));
        }

        private string DefaultJsonPath()
        {
            var skeletonPath = AssetDatabase.GetAssetPath(_skeleton);
            var directory = Path.GetDirectoryName(skeletonPath)?.Replace('\\', '/') ?? "Assets";
            return $"{directory}/{_skeleton.name}{JsonSuffix}.json";
        }

        #endregion

        #region Saving

        private void Save()
        {
            if (_json == null || _layout == null) return;

            if (_skeleton != null) _layout.skeleton = _skeleton.name;

            var path = AssetDatabase.GetAssetPath(_json);
            File.WriteAllText(path, _layout.ToJson());
            AssetDatabase.ImportAsset(path);

            hasUnsavedChanges = false;
            Debug.Log($"[SkinLayout] раскладка сохранена в {path}.", _json);
        }

        private void CreateJson()
        {
            var suggested = DefaultJsonPath();
            var path = EditorUtility.SaveFilePanelInProject(
                "Create skin layout",
                Path.GetFileNameWithoutExtension(suggested),
                "json",
                "Где сохранить раскладку скинов",
                Path.GetDirectoryName(suggested));

            if (string.IsNullOrEmpty(path)) return;

            var layout = new SkinLayout { skeleton = _skeleton.name };
            File.WriteAllText(path, layout.ToJson());
            AssetDatabase.ImportAsset(path);

            _json = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            StoreGuid(JsonPrefKey, _json);
            Reload();
        }

        /// <summary>Перед сменой ассета: false — пользователь передумал.</summary>
        private bool ConfirmDiscard()
        {
            if (!hasUnsavedChanges) return true;

            int choice = EditorUtility.DisplayDialogComplex(
                "Skin Layout",
                "В раскладке есть несохранённые изменения.",
                "Сохранить", "Отмена", "Не сохранять");

            if (choice == 1) return false;
            if (choice == 0) Save();
            return true;
        }

        private void MarkDirty() => hasUnsavedChanges = true;

        #endregion

        #region GUI

        private void OnGUI()
        {
            DrawAssets();

            if (_skeleton == null)
            {
                EditorGUILayout.HelpBox("Укажи SkeletonDataAsset, скины которого нужно разложить.", MessageType.Info);
                return;
            }

            if (_json == null)
            {
                DrawCreateJson();
                return;
            }

            if (_layout == null) return;

            DrawToolbar();
            DrawSkeletonMismatch();

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawCategories();
                DrawSelectedCategory();
                DrawUnassigned();
                DrawAppearanceConfig();
            }
        }

        private void DrawAssets()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.BeginChangeCheck();
                var skeleton = (SkeletonDataAsset)EditorGUILayout.ObjectField(
                    "Skeleton", _skeleton, typeof(SkeletonDataAsset), false);

                if (EditorGUI.EndChangeCheck() && ConfirmDiscard())
                {
                    _skeleton = skeleton;
                    StoreGuid(SkeletonPrefKey, _skeleton);

                    // Раскладка уже лежит рядом со скелетом — подхватываем сами,
                    // чтобы не предлагать создать вторую.
                    if (_json == null && _skeleton != null)
                    {
                        _json = AssetDatabase.LoadAssetAtPath<TextAsset>(DefaultJsonPath());
                        StoreGuid(JsonPrefKey, _json);
                    }

                    Reload();
                }

                EditorGUI.BeginChangeCheck();
                var json = (TextAsset)EditorGUILayout.ObjectField(
                    "Layout JSON", _json, typeof(TextAsset), false);

                if (EditorGUI.EndChangeCheck() && ConfirmDiscard())
                {
                    if (json != null && !AssetDatabase.GetAssetPath(json).EndsWith(".json"))
                    {
                        EditorUtility.DisplayDialog("Skin Layout", "Нужен файл .json.", "OK");
                        return;
                    }

                    _json = json;
                    StoreGuid(JsonPrefKey, _json);
                    Reload();
                }

                EditorGUI.BeginChangeCheck();
                var appearanceConfig = (SpineAppearanceConfig)EditorGUILayout.ObjectField(
                    "Appearance Config", _appearanceConfig, typeof(SpineAppearanceConfig), false);

                if (EditorGUI.EndChangeCheck())
                {
                    _appearanceConfig = appearanceConfig;
                    StoreGuid(AppearanceConfigPrefKey, _appearanceConfig);
                }

                if (_json == null) return;

                // Только показ: источник иконок не выбирается, а находится по JSON.
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.ObjectField(
                        new GUIContent("Icons → Layout Source",
                            "SkinLayoutSource, который читает игра для этого JSON. В него пишутся иконки скинов."),
                        _iconSource, typeof(SkinLayoutSourceSO), false);

                if (_iconSource == null)
                    EditorGUILayout.HelpBox(
                        "Этот JSON не указан ни в одном SkinLayoutSource — иконки скинов хранить негде. " +
                        "Укажи его в поле Layout у SkinLayoutSource, который загружает игра.",
                        MessageType.Warning);
            }
        }

        /// <summary>
        /// Скины, зоны перекраса и правила перекрытия персонажа — прямо здесь, а не
        /// в отдельном инспекторе ассета. Раскладка категорий и внешний вид персонажа
        /// настраиваются вместе, и переключаться между окном и Project-панелью ради
        /// одной правки цвета зоны было лишним кликом.
        /// </summary>
        private void DrawAppearanceConfig()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(AppearanceConfigWidth)))
            {
                GUILayout.Label("Внешний вид (SpineAppearanceConfig)", EditorStyles.boldLabel);

                if (_appearanceConfig == null)
                {
                    EditorGUILayout.HelpBox(
                        "Назначь SpineAppearanceConfig выше, чтобы править скины, зоны " +
                        "перекраса и правила перекрытия здесь же.",
                        MessageType.Info);
                    return;
                }

                var serialized = new SerializedObject(_appearanceConfig);
                serialized.Update();

                // Отдельный скролл: список скинов растёт с числом категорий персонажа
                // и не должен раздувать окно так, что до Color Zones / Hide Rules
                // приходится пролистывать через всё остальное.
                EditorGUILayout.LabelField("Skins", EditorStyles.boldLabel);
                _appearanceSkinsScroll = EditorGUILayout.BeginScrollView(
                    _appearanceSkinsScroll, GUILayout.Height(140f));
                EditorGUILayout.PropertyField(serialized.FindProperty("_skins"), true);
                EditorGUILayout.EndScrollView();

                EditorGUILayout.Space();

                // Второй скролл — остальной конфиг: скелет для выпадающих списков,
                // зоны перекраса (у каждой свой список слотов) и правила перекрытия.
                EditorGUILayout.LabelField("Config", EditorStyles.boldLabel);
                _appearanceDetailsScroll = EditorGUILayout.BeginScrollView(
                    _appearanceDetailsScroll, GUILayout.Height(240f));
                EditorGUILayout.PropertyField(serialized.FindProperty("_skeletonData"));
                EditorGUILayout.PropertyField(serialized.FindProperty("_colorZones"), true);
                EditorGUILayout.PropertyField(serialized.FindProperty("_hideRules"), true);
                EditorGUILayout.EndScrollView();

                serialized.ApplyModifiedProperties();
            }
        }

        private void DrawCreateJson()
        {
            EditorGUILayout.HelpBox(
                "У скелета нет файла раскладки. Создай новый JSON или укажи существующий в поле выше.",
                MessageType.Warning);

            if (GUILayout.Button("Создать JSON", GUILayout.Height(28f)))
                CreateJson();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var assigned = AssignedFolders();
                GUILayout.Label(
                    $"Групп в скелете: {_groups.Count}   разложено: {_groups.Keys.Count(assigned.Contains)}" +
                    $"   категорий: {_layout.categories.Count}",
                    EditorStyles.miniLabel);

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Обновить скелет", EditorStyles.toolbarButton))
                {
                    _groups = LoadGroups(_skeleton);
                    _slotNames = LoadSlotNames(_skeleton);
                }

                using (new EditorGUI.DisabledScope(!hasUnsavedChanges))
                {
                    if (GUILayout.Button("Откатить", EditorStyles.toolbarButton) &&
                        EditorUtility.DisplayDialog("Skin Layout", "Отменить все несохранённые изменения?", "Да", "Нет"))
                        Reload();

                    if (GUILayout.Button(hasUnsavedChanges ? "Сохранить*" : "Сохранить", EditorStyles.toolbarButton))
                        Save();
                }
            }
        }

        private void DrawSkeletonMismatch()
        {
            if (string.IsNullOrEmpty(_layout.skeleton) || _layout.skeleton == _skeleton.name) return;

            EditorGUILayout.HelpBox(
                $"JSON сделан для скелета '{_layout.skeleton}', а выбран '{_skeleton.name}'. " +
                "При сохранении раскладка привяжется к выбранному.",
                MessageType.Warning);
        }

        private void DrawCategories()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(CategoriesWidth)))
            {
                _categoriesScroll = EditorGUILayout.BeginScrollView(_categoriesScroll);
                _categoryList.DoLayoutList();
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawSelectedCategory()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
            {
                var category = SelectedCategory();
                if (category == null)
                {
                    EditorGUILayout.HelpBox("Создай категорию слева и выбери её.", MessageType.Info);
                    return;
                }

                EditorGUI.BeginChangeCheck();
                category.id = EditorGUILayout.TextField("Id", category.id);
                category.title = EditorGUILayout.TextField("Title", category.title);
                if (EditorGUI.EndChangeCheck()) MarkDirty();

                DrawCategoryWarnings(category);

                _groupsScroll = EditorGUILayout.BeginScrollView(_groupsScroll);
                _groupList?.DoLayoutList();
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawCategoryWarnings(SkinLayout.Category category)
        {
            if (string.IsNullOrWhiteSpace(category.id))
                EditorGUILayout.HelpBox("У категории пустой id.", MessageType.Error);
            else if (_layout.categories.Count(c => c.id == category.id) > 1)
                EditorGUILayout.HelpBox($"Id '{category.id}' повторяется.", MessageType.Error);

            var missing = category.groups.Where(g => !_groups.ContainsKey(g.folder)).Select(g => g.folder).ToList();
            if (missing.Count > 0)
                EditorGUILayout.HelpBox(
                    $"В скелете нет папок: {string.Join(", ", missing)}. Удали их или проверь имена в Spine.",
                    MessageType.Warning);

            var shared = category.groups
                .Where(g => _layout.categories.Any(c => c != category && c.groups.Any(o => o.folder == g.folder)))
                .Select(g => g.folder)
                .ToList();
            if (shared.Count > 0)
                EditorGUILayout.HelpBox(
                    $"Папки лежат ещё и в других категориях: {string.Join(", ", shared)}.",
                    MessageType.Warning);

            // Новичок одет в Owned From Start (так игра одевает нового игрока): группа без
            // них на старте пуста — для шапки это нормально, для деки скейта вряд ли.
            var withoutStart = category.groups
                .Where(g => !g.skins.Any(o => o != null && o.availability == SkinAvailability.OwnedFromStart))
                .Select(g => g.canBeOff ? g.folder : $"{g.folder} (Can Be Off выкл. — наденется резерв)")
                .ToList();
            if (withoutStart.Count > 0)
                EditorGUILayout.HelpBox(
                    $"Нет Owned From Start: {string.Join(", ", withoutStart)}. Новый игрок " +
                    "начнёт без варианта этих групп.",
                    MessageType.Warning);

            if (!HasDefault(category))
                EditorGUILayout.HelpBox(
                    "В категории нет скина Default — резервом станет первый открытый с начала. " +
                    "Резерв надевается, когда надетый скин пропал, а у группы нет своего Owned From Start.",
                    MessageType.Info);
        }

        /// <summary>Есть ли в категории скин, помеченный Default — резерв категории.</summary>
        private bool HasDefault(SkinLayout.Category category)
        {
            foreach (var group in category.groups)
            foreach (var over in group.skins)
            {
                if (over != null && over.availability == SkinAvailability.Default) return true;
            }

            return false;
        }

        private void DrawUnassigned()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(UnassignedWidth)))
            {
                var category = SelectedCategory();
                var assigned = AssignedFolders();
                var free = _groups.Keys
                    .Where(f => !assigned.Contains(f))
                    .Where(f => string.IsNullOrEmpty(_search) ||
                                f.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

                GUILayout.Label($"Не разложено ({free.Count})", EditorStyles.boldLabel);
                _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);

                using (new EditorGUI.DisabledScope(category == null || free.Count == 0))
                {
                    if (GUILayout.Button(category == null ? "Выбери категорию" : $"Все → {Label(category)}"))
                    {
                        foreach (var folder in free) AddGroup(category, folder);
                    }
                }

                _unassignedScroll = EditorGUILayout.BeginScrollView(_unassignedScroll);

                foreach (var folder in free)
                    DrawFreeGroup(folder, category);

                if (free.Count == 0)
                    EditorGUILayout.HelpBox(_groups.Count == 0 ? "В скелете нет скинов." : "Всё разложено.", MessageType.Info);

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawFreeGroup(string folder, SkinLayout.Category category)
        {
            var skins = _groups[folder];

            using (new EditorGUILayout.HorizontalScope())
            {
                bool expanded = _expanded.Contains(folder);
                bool now = EditorGUILayout.Foldout(expanded, $"{folder}  ({skins.Count})", true);
                if (now != expanded)
                {
                    if (now) _expanded.Add(folder);
                    else _expanded.Remove(folder);
                }

                using (new EditorGUI.DisabledScope(category == null))
                {
                    if (GUILayout.Button(new GUIContent("→", "Добавить в выбранную категорию"), GUILayout.Width(28f)))
                        AddGroup(category, folder);
                }
            }

            if (!_expanded.Contains(folder)) return;

            using (new EditorGUI.IndentLevelScope())
            {
                foreach (var skin in skins)
                    EditorGUILayout.LabelField(skin, EditorStyles.miniLabel);
            }
        }

        #endregion

        #region Lists

        private void BuildCategoryList()
        {
            if (_layout == null)
            {
                _categoryList = null;
                return;
            }

            _categoryList = new ReorderableList(_layout.categories, typeof(SkinLayout.Category), true, true, true, true)
            {
                index = _selected,
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Категории"),
                drawElementCallback = (rect, index, active, focused) =>
                {
                    var category = _layout.categories[index];
                    rect.y += 2f;
                    rect.height = EditorGUIUtility.singleLineHeight;
                    EditorGUI.LabelField(rect, Label(category), $"{category.groups.Count}");
                },
                onSelectCallback = list => Select(list.index),
                onAddCallback = list =>
                {
                    int number = _layout.categories.Count + 1;
                    _layout.categories.Add(new SkinLayout.Category
                    {
                        id = $"category_{number}",
                        title = $"Category {number}",
                    });
                    list.index = _layout.categories.Count - 1;
                    Select(list.index);
                    MarkDirty();
                },
                onRemoveCallback = list =>
                {
                    var category = _layout.categories[list.index];
                    if (category.groups.Count > 0 && !EditorUtility.DisplayDialog(
                            "Skin Layout",
                            $"Удалить категорию '{Label(category)}'? Её папки ({category.groups.Count}) вернутся в неразложенные.",
                            "Удалить", "Отмена"))
                        return;

                    _layout.categories.RemoveAt(list.index);
                    list.index = Mathf.Min(list.index, _layout.categories.Count - 1);
                    Select(list.index);
                    MarkDirty();
                },
                onReorderCallback = list =>
                {
                    Select(list.index);
                    MarkDirty();
                },
            };
        }

        private void BuildGroupList()
        {
            var category = SelectedCategory();
            if (category == null)
            {
                _groupList = null;
                return;
            }

            _groupList = new ReorderableList(category.groups, typeof(SkinLayout.Group), true, true, false, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(
                    rect, "Группы (папка в Spine → подпись в UI; ▸ — скины, иконки и перекрытия)"),
                elementHeightCallback = index => GroupElementHeight(category.groups[index]),
                drawElementCallback = (rect, index, active, focused) => DrawGroupElement(rect, category.groups[index]),
                onRemoveCallback = list =>
                {
                    category.groups.RemoveAt(list.index);
                    list.index = Mathf.Min(list.index, category.groups.Count - 1);
                    MarkDirty();
                },
                onReorderCallback = _ => MarkDirty(),
            };
        }

        private float GroupElementHeight(SkinLayout.Group group)
        {
            // Заголовок + Color Zones + Can Be Off / Multi Select — видны всегда, не только при
            // развёрнутой группе: иначе про кнопку color в UI забывали бы точно
            // так же, как раньше забыли про зоны на превью-скелете меню.
            var height = 3 * (EditorGUIUtility.singleLineHeight + 4f);

            if (_expandedGroups.Contains(group.folder) && _groups.TryGetValue(group.folder, out var skins))
                height += skins.Count * SkinRowHeight;

            return height;
        }

        private void DrawGroupElement(Rect rect, SkinLayout.Group group)
        {
            var headerRect = new Rect(rect.x, rect.y + 2f, rect.width, EditorGUIUtility.singleLineHeight);
            var foldRect = new Rect(headerRect.x, headerRect.y, 16f, headerRect.height);
            var folderRect = new Rect(headerRect.x + 18f, headerRect.y, headerRect.width * 0.45f - 18f, headerRect.height);
            var titleRect = new Rect(headerRect.x + headerRect.width * 0.45f, headerRect.y, headerRect.width * 0.55f, headerRect.height);

            var hasSkins = _groups.TryGetValue(group.folder, out var skins) && skins.Count > 0;
            var expanded = _expandedGroups.Contains(group.folder);

            using (new EditorGUI.DisabledScope(!hasSkins))
            {
                var now = EditorGUI.Foldout(foldRect, expanded, GUIContent.none, true);
                if (now != expanded)
                {
                    if (now) _expandedGroups.Add(group.folder);
                    else _expandedGroups.Remove(group.folder);
                }
            }

            if (skins != null)
            {
                EditorGUI.LabelField(folderRect, new GUIContent(
                    $"{group.folder}  ({skins.Count})", string.Join("\n", skins)));
            }
            else
            {
                var previous = GUI.color;
                GUI.color = new Color(1f, 0.45f, 0.45f);
                EditorGUI.LabelField(folderRect, new GUIContent($"{group.folder}  (нет в скелете)"));
                GUI.color = previous;
            }

            EditorGUI.BeginChangeCheck();
            group.title = EditorGUI.TextField(titleRect, group.title);
            if (EditorGUI.EndChangeCheck()) MarkDirty();

            var colorZonesRect = new Rect(rect.x + 20f, headerRect.yMax + 2f, rect.width - 20f, EditorGUIUtility.singleLineHeight);
            DrawColorZones(colorZonesRect, group);

            // Обе галки группы — в одной строке, Multi Select следом за Can Be Off.
            var flagsRect = new Rect(rect.x + 20f, colorZonesRect.yMax + 2f, rect.width - 20f, EditorGUIUtility.singleLineHeight);
            DrawCanBeOff(new Rect(flagsRect.x, flagsRect.y, 110f, flagsRect.height), group);
            DrawMultiSelect(new Rect(flagsRect.x + 116f, flagsRect.y, 110f, flagsRect.height), group);

            if (!expanded || skins == null) return;

            var y = flagsRect.yMax + 2f;

            foreach (var skinName in skins)
            {
                DrawSkinRow(new Rect(rect.x + 20f, y, rect.width - 20f, SkinRowHeight), group, skinName);
                y += SkinRowHeight;
            }
        }

        /// <summary>
        /// Зоны SpineAppearanceConfig.ColorZone, которые красит кнопка «color» этой
        /// группы в панели кастомизации — через запятую. Имена не проверяются:
        /// в отличие от папок и скинов, зоны заведены не на скелете, а в ассете
        /// внешнего вида персонажа (SpineAppearanceConfig), и этот редактор про
        /// тот ассет ничего не знает. Пусто — кнопки color у группы не будет.
        /// </summary>
        private void DrawColorZones(Rect rect, SkinLayout.Group group)
        {
            var labelRect = new Rect(rect.x, rect.y, 84f, rect.height);
            var fieldRect = new Rect(rect.x + 86f, rect.y, rect.width - 86f, rect.height);

            EditorGUI.LabelField(labelRect, new GUIContent(
                "Color Zones",
                "Зоны SpineAppearanceConfig.ColorZone, которые кнопка color этой группы " +
                "красит разом одним цветом. Через запятую, пусто — кнопки не будет."),
                EditorStyles.miniLabel);

            var joined = string.Join(", ", group.colorZones ?? new List<string>());

            EditorGUI.BeginChangeCheck();
            var edited = EditorGUI.TextField(fieldRect, joined);
            if (!EditorGUI.EndChangeCheck()) return;

            group.colorZones = edited
                .Split(',')
                .Select(zone => zone.Trim())
                .Where(zone => zone.Length > 0)
                .ToList();

            MarkDirty();
        }

        /// <summary>
        /// Можно ли снять последний вариант группы, оставив её пустой — повторным
        /// нажатием на уже надетый вариант. Выключи для групп вроде базовой головы
        /// или доски: там всегда должен быть выбран ровно один вариант. Аксессуары
        /// (шапка, украшение) обычно снимаемы — оставляй включённым.
        /// </summary>
        private void DrawCanBeOff(Rect rect, SkinLayout.Group group)
        {
            var labelRect = new Rect(rect.x, rect.y, 84f, rect.height);
            var toggleRect = new Rect(rect.x + 86f, rect.y, 16f, rect.height);

            EditorGUI.LabelField(labelRect, new GUIContent(
                "Can Be Off",
                "Можно ли снять последний вариант группы, оставив её пустой. Выключи для " +
                "групп, у которых всегда должен быть выбран ровно один вариант (голова, доска)."),
                EditorStyles.miniLabel);

            EditorGUI.BeginChangeCheck();
            var canBeOff = EditorGUI.Toggle(toggleRect, group.canBeOff);
            if (!EditorGUI.EndChangeCheck()) return;

            group.canBeOff = canBeOff;
            MarkDirty();
        }

        /// <summary>
        /// Можно ли надеть несколько вариантов группы сразу — для аксессуаров. Нажатие
        /// на вариант тогда добавляет или снимает только его, а не заменяет прежний.
        /// Варианты должны сидеть в разных слотах Spine: в одном слоте виден только
        /// один аттачмент, и второй аксессуар молча пропал бы.
        /// </summary>
        private void DrawMultiSelect(Rect rect, SkinLayout.Group group)
        {
            var labelRect = new Rect(rect.x, rect.y, 84f, rect.height);
            var toggleRect = new Rect(rect.x + 86f, rect.y, 16f, rect.height);

            EditorGUI.LabelField(labelRect, new GUIContent(
                "Multi Select",
                "Можно надеть несколько вариантов группы сразу (аксессуары): нажатие добавляет " +
                "или снимает только этот вариант. Варианты должны сидеть в разных слотах Spine."),
                EditorStyles.miniLabel);

            EditorGUI.BeginChangeCheck();
            var multiSelect = EditorGUI.Toggle(toggleRect, group.multiSelect);
            if (!EditorGUI.EndChangeCheck()) return;

            group.multiSelect = multiSelect;
            MarkDirty();
        }

        /// <summary>
        /// Одна строка скина: иконка слева, имя из скелета (не редактируется — оно и
        /// есть ключ), подпись в UI и то, как игрок его получает. Ничего не заполнено —
        /// скин попадёт в каталог как Locked, с именем из Spine вместо подписи.
        /// </summary>
        private void DrawSkinRow(Rect rect, SkinLayout.Group group, string skinName)
        {
            var over = SkinLayout.FindOverride(group, skinName);

            // Иконка — квадрат на всю высоту строки, остальные поля сдвигаются вправо.
            var iconSize = SkinRowHeight - 6f;
            DrawSkinIcon(new Rect(rect.x, rect.y, iconSize, iconSize), skinName);
            rect = new Rect(rect.x + iconSize + 6f, rect.y, rect.width - iconSize - 6f, rect.height);

            var lineHeight = EditorGUIUtility.singleLineHeight;
            var nameRect = new Rect(rect.x, rect.y, rect.width * 0.4f, lineHeight);
            var titleRect = new Rect(rect.x + rect.width * 0.4f, rect.y, rect.width * 0.3f, lineHeight);
            var availRect = new Rect(rect.x + rect.width * 0.7f, rect.y, rect.width * 0.3f, lineHeight);

            // Отключённый скин виден сразу: серое имя с пометкой — иначе среди десятков
            // строк легко забыть, что скин выключен, и искать, почему его нет в игре.
            if (over != null && over.availability == SkinAvailability.Disabled)
            {
                var previous = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.45f);
                EditorGUI.LabelField(nameRect, new GUIContent(
                    $"{skinName}  (откл.)",
                    "Disabled: скина нет в игре — ни в кастомизации, ни в выдаче. " +
                    "У игроков, которым он уже выдан, он сохраняется и вернётся при включении."),
                    EditorStyles.miniLabel);
                GUI.color = previous;
            }
            else
            {
                EditorGUI.LabelField(nameRect, skinName, EditorStyles.miniLabel);
            }

            EditorGUI.BeginChangeCheck();
            var title = EditorGUI.TextField(titleRect, over?.title ?? string.Empty);
            var availability = (SkinAvailability)EditorGUI.EnumPopup(
                availRect, over?.availability ?? SkinAvailability.Locked);

            if (EditorGUI.EndChangeCheck())
                SetOverride(group, skinName, title, availability);

            var buttonRect = new Rect(rect.x, rect.y + lineHeight + 2f, rect.width, lineHeight);
            DrawHideRuleButton(buttonRect, skinName);

            var colorRect = new Rect(rect.x, rect.y + 2 * (lineHeight + 2f), rect.width, lineHeight);
            DrawDefaultColor(colorRect, group, skinName);
        }

        /// <summary>
        /// Иконка скина в панели кастомизации — своя у каждого скина, а не у группы
        /// или категории. Пишется не в раскладку, а в <see cref="_iconSource"/> —
        /// сразу и с Undo, как правила перекрытия в Appearance Config, — и тут же
        /// сохраняется на диск: кнопка «Сохранить» окна про JSON и на смену одной
        /// иконки не включается, а терять иконку из-за этого нельзя.
        /// </summary>
        private void DrawSkinIcon(Rect rect, string skinName)
        {
            var tooltip = _iconSource == null
                ? "Этот JSON не указан ни в одном SkinLayoutSource — иконку хранить негде."
                : "Иконка скина в панели кастомизации. Перетащи спрайт; пусто — без иконки.";

            using (new EditorGUI.DisabledScope(_iconSource == null))
            {
                var icon = _iconSource != null ? _iconSource.GetIcon(skinName) : null;

                EditorGUI.BeginChangeCheck();
                // Поле выше строки — Unity сама рисует его превью-квадратом.
                var edited = (Sprite)EditorGUI.ObjectField(rect, icon, typeof(Sprite), false);
                if (EditorGUI.EndChangeCheck())
                    SetSkinIcon(skinName, edited);
            }

            GUI.Label(rect, new GUIContent(string.Empty, tooltip));
        }

        private void SetSkinIcon(string skinName, Sprite icon)
        {
            if (_iconSource == null) return;

            Undo.RecordObject(_iconSource, "Set Skin Icon");
            _iconSource.SetIcon(skinName, icon);
            EditorUtility.SetDirty(_iconSource);
            AssetDatabase.SaveAssetIfDirty(_iconSource);
        }

        /// <summary>
        /// Цвет, в который красятся зоны группы этого скина сразу, как только его
        /// выбирают в кастомизации. Галка
        /// отдельно от цвета: не задано — зоны остаются такими, какими были, а не
        /// сбрасываются в белый при каждом выборе скина без своего цвета.
        /// </summary>
        private void DrawDefaultColor(Rect rect, SkinLayout.Group group, string skinName)
        {
            var over = SkinLayout.FindOverride(group, skinName);
            var hasDefault = over?.hasDefaultColor ?? false;
            var color = over?.defaultColor ?? Color.white;

            var toggleRect = new Rect(rect.x, rect.y, 16f, rect.height);
            var colorFieldRect = new Rect(rect.x + 20f, rect.y, rect.width - 20f, rect.height);

            EditorGUI.BeginChangeCheck();

            var newHasDefault = EditorGUI.Toggle(toggleRect, new GUIContent(
                "", "Красить зоны группы в этот цвет сразу при выборе скина."), hasDefault);

            Color newColor;
            using (new EditorGUI.DisabledScope(!newHasDefault))
                newColor = EditorGUI.ColorField(colorFieldRect, color);

            if (EditorGUI.EndChangeCheck())
                SetDefaultColor(group, skinName, newHasDefault, newColor);
        }

        private void SetDefaultColor(SkinLayout.Group group, string skinName, bool hasDefault, Color color)
        {
            var existing = SkinLayout.FindOverride(group, skinName);

            if (existing == null)
            {
                if (!hasDefault) return;

                existing = new SkinLayout.SkinOverride { skin = skinName };
                group.skins.Add(existing);
            }

            existing.hasDefaultColor = hasDefault;
            existing.defaultColor = color;

            if (!hasDefault && existing.availability == SkinAvailability.Locked &&
                string.IsNullOrWhiteSpace(existing.title))
                group.skins.Remove(existing);

            MarkDirty();
        }

        /// <summary>
        /// Кнопка правила перекрытия (<see cref="SpineAppearanceConfig.HideRule"/>):
        /// какие слоты гаснут, пока этот скин надет. Слоты, а не другие скины —
        /// конфликт между вещами визуальный (борода торчит из-под шлема), и решать
        /// его нужно тем же способом, каким его решает сам рантайм, а не отдельным правилом
        /// «нельзя надеть одновременно».
        /// </summary>
        private void DrawHideRuleButton(Rect rect, string skinName)
        {
            using (new EditorGUI.DisabledScope(_appearanceConfig == null))
            {
                var rule = _appearanceConfig != null ? _appearanceConfig.FindHideRule(skinName) : null;
                var slots = rule?.Slots;
                var count = slots?.Length ?? 0;
                var label = count == 0 ? "Прячет: ничего" : $"Прячет ({count}): {string.Join(", ", slots)}";
                var tooltip = _appearanceConfig == null
                    ? "Назначь Appearance Config, чтобы настраивать перекрытие слотов."
                    : "Слоты скелета, которые гаснут, пока этот скин надет (SpineAppearanceConfig.HideRule).";

                if (EditorGUI.DropdownButton(rect, new GUIContent(label, tooltip), FocusType.Passive))
                    ShowHideSlotsMenu(skinName);
            }
        }

        private void ShowHideSlotsMenu(string skinName)
        {
            if (_appearanceConfig == null) return;

            var rule = _appearanceConfig.FindHideRule(skinName);
            var current = rule?.Slots ?? new string[0];

            var menu = new GenericMenu();

            foreach (var slotName in _slotNames)
            {
                var capturedSlot = slotName;
                var isHidden = Array.IndexOf(current, capturedSlot) >= 0;
                menu.AddItem(new GUIContent(capturedSlot), isHidden, () => ToggleHideSlot(skinName, capturedSlot));
            }

            foreach (var slotName in current)
            {
                if (_slotNames.Contains(slotName)) continue;
                var staleSlot = slotName;
                menu.AddItem(new GUIContent($"Удалить отсутствующий/{staleSlot}"), true,
                    () => ToggleHideSlot(skinName, staleSlot));
            }

            if (_slotNames.Count == 0)
                menu.AddDisabledItem(new GUIContent("В скелете нет слотов"));

            menu.ShowAsContext();
        }

        private void ToggleHideSlot(string skinName, string slotName)
        {
            if (_appearanceConfig == null) return;

            Undo.RecordObject(_appearanceConfig, "Toggle Hide Slot");

            var rule = _appearanceConfig.GetOrAddHideRule(skinName);
            var slots = new List<string>(rule.Slots);

            if (!slots.Remove(slotName))
                slots.Add(slotName);

            rule.Slots = slots.ToArray();

            if (rule.Slots.Length == 0)
                _appearanceConfig.RemoveHideRule(rule);

            EditorUtility.SetDirty(_appearanceConfig);
            Repaint();
        }

        /// <summary>
        /// Записать правку скина — или снести её, если она вернулась к дефолту
        /// (Locked без подписи): раскладка хранит только отличия от того, что и
        /// так следует из Spine.
        /// </summary>
        private void SetOverride(SkinLayout.Group group, string skinName, string title, SkinAvailability availability)
        {
            var existing = SkinLayout.FindOverride(group, skinName);
            var isDefault = availability == SkinAvailability.Locked && string.IsNullOrWhiteSpace(title) &&
                            !(existing?.hasDefaultColor ?? false);

            if (isDefault)
            {
                if (existing != null) group.skins.Remove(existing);
            }
            else if (existing != null)
            {
                existing.title = title;
                existing.availability = availability;
            }
            else
            {
                group.skins.Add(new SkinLayout.SkinOverride
                {
                    skin = skinName,
                    title = title,
                    availability = availability,
                });
            }

            MarkDirty();
        }

        private void Select(int index)
        {
            _selected = _layout != null && index >= 0 && index < _layout.categories.Count ? index : -1;
            BuildGroupList();
            Repaint();
        }

        #endregion

        #region Helpers

        private SkinLayout.Category SelectedCategory()
        {
            return _layout != null && _selected >= 0 && _selected < _layout.categories.Count
                ? _layout.categories[_selected]
                : null;
        }

        private HashSet<string> AssignedFolders()
        {
            var assigned = new HashSet<string>();
            if (_layout == null) return assigned;

            foreach (var category in _layout.categories)
            foreach (var group in category.groups)
                assigned.Add(group.folder);

            return assigned;
        }

        private void AddGroup(SkinLayout.Category category, string folder)
        {
            if (category == null || category.groups.Any(g => g.folder == folder)) return;

            category.groups.Add(new SkinLayout.Group { folder = folder, title = folder.Replace('_', ' ') });
            MarkDirty();
        }

        private static string Label(SkinLayout.Category category)
        {
            return string.IsNullOrWhiteSpace(category.title) ? category.id : category.title;
        }

        #endregion
    }
}
#endif
