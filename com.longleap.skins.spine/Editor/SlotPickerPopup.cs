#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Spine;
using Spine.Unity;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditorInternal;
using UnityEngine;
using Event = UnityEngine.Event;

namespace LongLeap.Skins.EditorTools
{
    /// <summary>
    /// Окно выбора слотов скелета — для зон перекраса и правил перекрытия
    /// <see cref="SpineAppearanceConfig"/>.
    ///
    /// Заменяет GenericMenu: в скелете сотня слотов, а длинное системное меню не ищет,
    /// не листается колесом и закрывается после каждого клика. Здесь есть поиск, слоты
    /// сгруппированы по костям, галочки ставятся по одной или пачкой, и окно открыто,
    /// пока не кликнуть мимо него.
    ///
    /// Чей список правится, окно не знает: оно читает и пишет его через делегаты.
    /// Каждая галочка пишется сразу (Undo — забота владельца списка), поэтому выбранное
    /// перечитывается на каждом кадре — и после Ctrl+Z окно показывает правду.
    /// </summary>
    internal sealed class SlotPickerPopup : PopupWindowContent
    {
        private const float Width = 360f;
        private const float Height = 480f;

        // Отступы строки кости: стрелка, галочка «вся кость», имя.
        private const float ArrowWidth = 14f;
        private const float ToggleWidth = 16f;

        /// <summary>Галочка слота стоит под именем кости, а не под её галочкой.</summary>
        private const float SlotIndent = 4f + ArrowWidth + ToggleWidth + 4f;

        private const int FilterAll = 0;
        private const int FilterSelected = 1;

        private static readonly Color HoverColor = new Color(0.5f, 0.5f, 0.5f, 0.15f);
        private static readonly Color MissingColor = new Color(0.9f, 0.35f, 0.3f);

        /// <summary>
        /// Какие кости раскрыты — общее для всех открытий окна за сессию редактора:
        /// когда зоны настраиваются одна за другой, нужные кости не приходится
        /// раскрывать заново.
        /// </summary>
        private static readonly HashSet<string> ExpandedBones = new HashSet<string>(StringComparer.Ordinal);

        private static GUIStyle _missingStyle;

        private readonly string _title;
        private readonly Func<IReadOnlyList<string>> _read;
        private readonly Action<string[]> _write;

        private readonly List<BoneGroup> _bones = new List<BoneGroup>();
        private readonly HashSet<string> _known = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Фильтры списка: «все», «отмеченные», затем по папке скинов. У первых двух
        /// набора слотов нет (null), у папки — слоты, в которые её скины кладут картинки.
        /// </summary>
        private readonly List<string> _filterLabels = new List<string>();
        private readonly List<HashSet<string>> _filterSlots = new List<HashSet<string>>();

        /// <summary>Слоты одной кости, прошедшие фильтр и поиск, — буфер на кадр.</summary>
        private readonly List<string> _visible = new List<string>();

        private SearchField _searchField;
        private string _search = string.Empty;
        private int _filter = FilterAll;
        private Vector2 _scroll;

        private sealed class BoneGroup
        {
            public string Bone;
            public readonly List<string> Slots = new List<string>();
        }

        private static GUIStyle MissingStyle => _missingStyle ??= new GUIStyle(EditorStyles.label)
        {
            normal = { textColor = MissingColor },
            hover = { textColor = MissingColor },
        };

        /// <summary>
        /// Открыть окно под кнопкой <paramref name="activator"/>. Вызывать из OnGUI —
        /// как и любой <see cref="PopupWindow"/>, он прерывает текущий проход GUI.
        /// </summary>
        /// <param name="activator">Кнопка, под которой открывается окно.</param>
        /// <param name="skeleton">Скелет, чьи слоты предлагаются.</param>
        /// <param name="title">Что правится — строка над поиском («Зона «Hair»»).</param>
        /// <param name="read">Текущий список слотов; зовётся на каждом кадре окна.</param>
        /// <param name="write">Записать новый список целиком, с Undo.</param>
        public static void Show(
            Rect activator,
            SkeletonDataAsset skeleton,
            string title,
            Func<IReadOnlyList<string>> read,
            Action<string[]> write)
        {
            var data = skeleton != null ? skeleton.GetSkeletonData(true) : null;
            if (data == null)
            {
                Debug.LogWarning("[SkinLayout] не удалось прочитать скелет — слоты выбрать не из чего.", skeleton);
                return;
            }

            PopupWindow.Show(activator, new SlotPickerPopup(data, title, read, write));
        }

        private SlotPickerPopup(
            SkeletonData data,
            string title,
            Func<IReadOnlyList<string>> read,
            Action<string[]> write)
        {
            _title = title;
            _read = read;
            _write = write;

            CollectBones(data);
            CollectFilters(data);
        }

        public override Vector2 GetWindowSize() => new Vector2(Width, Height);

        public override void OnOpen()
        {
            _searchField = new SearchField();
            _searchField.SetFocus();

            // Подсветка строки под курсором.
            editorWindow.wantsMouseMove = true;
        }

        #region Model

        /// <summary>Слоты по костям: кости — в порядке скелета, слоты — в порядке отрисовки.</summary>
        private void CollectBones(SkeletonData data)
        {
            var byBone = new Dictionary<BoneData, BoneGroup>();

            foreach (var bone in data.Bones)
            {
                var group = new BoneGroup { Bone = bone.Name };
                byBone.Add(bone, group);
                _bones.Add(group);
            }

            foreach (var slot in data.Slots)
            {
                _known.Add(slot.Name);

                if (byBone.TryGetValue(slot.BoneData, out var group))
                    group.Slots.Add(slot.Name);
            }

            _bones.RemoveAll(group => group.Slots.Count == 0);
        }

        /// <summary>
        /// Фильтры по папкам скинов: зона Boots красит слоты, которые занимают ботинки, —
        /// фильтр «Boots» показывает ровно их. Отдельно — скин default, базовое тело.
        /// </summary>
        private void CollectFilters(SkeletonData data)
        {
            _filterLabels.Add("Все слоты");
            _filterSlots.Add(null);

            _filterLabels.Add("Отмеченные");
            _filterSlots.Add(null);

            if (data.DefaultSkin != null)
            {
                _filterLabels.Add($"Слоты скинов/{data.DefaultSkin.Name}");
                _filterSlots.Add(SlotsOf(data, new[] { data.DefaultSkin }));
            }

            foreach (var group in SkinLayout.CollectGroups(data))
            {
                var skins = new List<Skin>();
                foreach (var skinName in group.Value)
                    skins.Add(data.FindSkin(skinName));

                _filterLabels.Add($"Слоты скинов/{group.Key}");
                _filterSlots.Add(SlotsOf(data, skins));
            }
        }

        private static HashSet<string> SlotsOf(SkeletonData data, IEnumerable<Skin> skins)
        {
            var slots = new HashSet<string>(StringComparer.Ordinal);

            foreach (var skin in skins)
            {
                if (skin == null) continue;

                foreach (var entry in skin.Attachments)
                    slots.Add(data.Slots.Items[entry.SlotIndex].Name);
            }

            return slots;
        }

        /// <summary>
        /// Проходит ли слот фильтр и поиск. Поиск — подстроки через пробел, все сразу,
        /// без учёта регистра; совпадение с именем кости тоже считается, чтобы по
        /// «arm_L1» найти всё, что висит на этой кости.
        /// </summary>
        private bool Passes(string slot, string bone, HashSet<string> selected, string[] terms)
        {
            if (_filter == FilterSelected && !selected.Contains(slot)) return false;

            var only = _filterSlots[_filter];
            if (only != null && !only.Contains(slot)) return false;

            foreach (var term in terms)
            {
                var inSlot = slot.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
                var inBone = bone != null && bone.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!inSlot && !inBone) return false;
            }

            return true;
        }

        /// <summary>Отметить или снять слоты одной записью — один шаг Undo на всю пачку.</summary>
        private void SetMany(IEnumerable<string> slots, bool on)
        {
            var result = new List<string>(_read() ?? Array.Empty<string>());
            var changed = false;

            foreach (var slot in slots)
            {
                if (on)
                {
                    if (result.Contains(slot)) continue;
                    result.Add(slot);
                    changed = true;
                }
                else
                {
                    changed |= result.RemoveAll(existing => existing == slot) > 0;
                }
            }

            if (!changed) return;

            _write(result.ToArray());

            // Инспектор или окно раскладки за спиной должны сразу показать новый список.
            InternalEditorUtility.RepaintAllViews();
        }

        #endregion

        #region GUI

        public override void OnGUI(Rect rect)
        {
            var current = Event.current;

            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
            {
                // Первый Esc чистит поиск, второй закрывает окно.
                if (_search.Length > 0) _search = string.Empty;
                else editorWindow.Close();

                current.Use();
                return;
            }

            if (current.type == EventType.MouseMove)
                editorWindow.Repaint();

            var selectedList = _read() ?? Array.Empty<string>();
            var selected = new HashSet<string>(selectedList, StringComparer.Ordinal);
            var terms = _search.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

            // Список уже сужен — кости раскрыты все, иначе найденное пряталось бы
            // в свёрнутых костях.
            var narrowed = terms.Length > 0 || _filter != FilterAll;
            var found = new List<string>();

            DrawHeader(selected.Count);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            var any = DrawMissing(selectedList, selected, terms, found);
            foreach (var bone in _bones)
                any |= DrawBone(bone, selected, terms, narrowed, found);

            if (!any)
                EditorGUILayout.LabelField("Ничего не найдено.", EditorStyles.centeredGreyMiniLabel);

            EditorGUILayout.EndScrollView();

            DrawFooter(found, selected, narrowed);
        }

        private void DrawHeader(int selectedCount)
        {
            if (!string.IsNullOrEmpty(_title))
                GUILayout.Label(_title, EditorStyles.boldLabel);

            _search = _searchField.OnGUI(_search) ?? string.Empty;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Показать", GUILayout.Width(58f));

                var label = new GUIContent(
                    ShortLabel(_filterLabels[_filter]),
                    "Сузить список: только отмеченные слоты или только слоты, " +
                    "в которые кладут картинки скины одной папки.");

                var buttonRect = GUILayoutUtility.GetRect(label, EditorStyles.popup, GUILayout.ExpandWidth(true));
                if (EditorGUI.DropdownButton(buttonRect, label, FocusType.Passive, EditorStyles.popup))
                    ShowFilterMenu(buttonRect);

                GUILayout.Label($"Отмечено: {selectedCount}", EditorStyles.miniLabel, GUILayout.Width(78f));
            }
        }

        private void ShowFilterMenu(Rect buttonRect)
        {
            var menu = new GenericMenu();

            for (int i = 0; i < _filterLabels.Count; i++)
            {
                var index = i;
                menu.AddItem(new GUIContent(_filterLabels[i]), _filter == i, () =>
                {
                    _filter = index;
                    editorWindow.Repaint();
                });

                if (i == FilterSelected)
                    menu.AddSeparator(string.Empty);
            }

            menu.DropDown(buttonRect);
        }

        /// <summary>Подпись выбранного фильтра без пути подменю: «Boots», а не «Слоты скинов/Boots».</summary>
        private static string ShortLabel(string label)
        {
            var slash = label.LastIndexOf('/');
            return slash < 0 ? label : label.Substring(slash + 1);
        }

        /// <summary>
        /// Отмеченные слоты, которых нет в скелете: их переименовали или удалили в Spine.
        /// Показаны первыми — их нужно убрать или поправить, игра их не найдёт.
        /// </summary>
        private bool DrawMissing(IReadOnlyList<string> selectedList, HashSet<string> selected, string[] terms, List<string> found)
        {
            _visible.Clear();

            foreach (var slot in selectedList)
            {
                if (!_known.Contains(slot) && !_visible.Contains(slot) && Passes(slot, null, selected, terms))
                    _visible.Add(slot);
            }

            if (_visible.Count == 0) return false;

            found.AddRange(_visible);

            GUILayout.Label(
                new GUIContent(
                    "Нет в скелете",
                    "Слоты переименованы или удалены в Spine. Сними галочку, чтобы убрать слот из списка."),
                EditorStyles.boldLabel);

            foreach (var slot in _visible)
                DrawSlot(slot, true, missing: true);

            return true;
        }

        private bool DrawBone(BoneGroup bone, HashSet<string> selected, string[] terms, bool narrowed, List<string> found)
        {
            _visible.Clear();
            foreach (var slot in bone.Slots)
            {
                if (Passes(slot, bone.Bone, selected, terms))
                    _visible.Add(slot);
            }

            if (_visible.Count == 0) return false;

            found.AddRange(_visible);

            var count = 0;
            foreach (var slot in _visible)
            {
                if (selected.Contains(slot)) count++;
            }

            var expanded = narrowed || ExpandedBones.Contains(bone.Bone);

            var row = Row();
            var arrowRect = new Rect(row.x + 4f, row.y, ArrowWidth, row.height);
            var toggleRect = new Rect(arrowRect.xMax, row.y, ToggleWidth, row.height);
            var labelRect = new Rect(toggleRect.xMax + 4f, row.y, row.xMax - toggleRect.xMax - 4f, row.height);

            if (!narrowed)
            {
                var current = Event.current;
                var clicked = current.type == EventType.MouseDown && current.button == 0 &&
                              (arrowRect.Contains(current.mousePosition) || labelRect.Contains(current.mousePosition));

                if (clicked)
                {
                    if (!ExpandedBones.Remove(bone.Bone)) ExpandedBones.Add(bone.Bone);
                    current.Use();
                }

                if (current.type == EventType.Repaint)
                    EditorStyles.foldout.Draw(arrowRect, false, false, expanded, false);
            }

            // Галочка кости: все её найденные слоты разом. Часть отмечена — прочерк,
            // клик по нему отмечает остальные.
            EditorGUI.showMixedValue = count > 0 && count < _visible.Count;
            EditorGUI.BeginChangeCheck();
            var all = EditorGUI.Toggle(toggleRect, count == _visible.Count);
            EditorGUI.showMixedValue = false;

            if (EditorGUI.EndChangeCheck())
                SetMany(new List<string>(_visible), all);

            GUI.Label(labelRect, $"{bone.Bone}   {count}/{_visible.Count}", EditorStyles.boldLabel);

            if (!expanded) return true;

            foreach (var slot in _visible)
                DrawSlot(slot, selected.Contains(slot), missing: false);

            return true;
        }

        private void DrawSlot(string slot, bool on, bool missing)
        {
            var row = Row();
            var toggleRect = new Rect(row.x + SlotIndent, row.y, row.width - SlotIndent, row.height);

            EditorGUI.BeginChangeCheck();
            var value = EditorGUI.ToggleLeft(toggleRect, slot, on, missing ? MissingStyle : EditorStyles.label);

            if (EditorGUI.EndChangeCheck())
                SetMany(new[] { slot }, value);
        }

        private void DrawFooter(List<string> found, HashSet<string> selected, bool narrowed)
        {
            var allOn = true;
            var anyOn = false;

            foreach (var slot in found)
            {
                var on = selected.Contains(slot);
                allOn &= on;
                anyOn |= on;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                // Без поиска и фильтра «найденные» — весь скелет; отметить его целиком
                // почти наверняка ошибка, поэтому кнопка ждёт, пока список сузят.
                using (new EditorGUI.DisabledScope(!narrowed || allOn))
                {
                    if (GUILayout.Button(new GUIContent(
                            $"Отметить найденные ({found.Count})",
                            "Отметить все слоты, которые сейчас в списке. Доступно, когда список " +
                            "сужен поиском или фильтром.")))
                        SetMany(found, true);
                }

                using (new EditorGUI.DisabledScope(!anyOn))
                {
                    if (GUILayout.Button(new GUIContent(
                            "Снять найденные",
                            "Снять галочки со всех слотов, которые сейчас в списке.")))
                        SetMany(found, false);
                }
            }
        }

        /// <summary>Строка списка с подсветкой под курсором.</summary>
        private static Rect Row()
        {
            var row = GUILayoutUtility.GetRect(0f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));

            if (Event.current.type == EventType.Repaint && row.Contains(Event.current.mousePosition))
                EditorGUI.DrawRect(row, HoverColor);

            return row;
        }

        #endregion
    }
}
#endif
