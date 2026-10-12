using System;
using System.Collections.Generic;
using System.Linq;
using LongLeap.Core.Editor;
using UnityEditor;
using UnityEngine;

namespace LongLeap.BuildManager.Editor
{
    // Окно, в котором видно всю конфигурацию сборки сразу.
    //
    // Главное свойство: щелчок по галочке НИЧЕГО не пишет. Раньше каждый пункт
    // меню правил символы компиляции немедленно, поэтому смена площадки стоила
    // полдюжины нажатий с перекомпиляцией между каждым. Здесь правки копятся,
    // изменённые строки подсвечены, и Apply записывает всё одним разом — одна
    // запись дефайнов, одна перезагрузка домена.
    //
    // Собрать игру можно только из ЧИСТОГО состояния, и это не формальность:
    // пока правки не применены и не перекомпилированы, код под #if соберётся
    // по старым дефайнам — например, постпроцесс index.html у GamePix живёт
    // целиком под #if GAMEPIX и в такой сборке просто не сработает.
    public sealed class BuildManagerWindow : EditorWindow
    {
        private const string Title = "Build Manager";
        private const string DirtyTitle = "Build Manager*";

        // Приоритет 0 против 120 у Cloud Save Panel: разрыв больше десяти, и
        // Unity сама рисует разделитель между ними.
        [MenuItem("LongLeap/" + Title, false, 0)]
        private static void Open()
        {
            BuildManagerWindow window = GetWindow<BuildManagerWindow>();
            window.titleContent = new GUIContent(Title);
            window.minSize = new Vector2(460f, 400f);
        }

        // Отложенные правки СЕРИАЛИЗУЕМ. Это не машинерия продолжения после
        // перезагрузки домена, а обычная сериализация полей EditorWindow — та
        // же, что помнит раскрытые фолдауты в инспекторе. Без неё сохранение
        // любого чужого скрипта молча выбрасывало бы подготовленную
        // конфигурацию.
        [SerializeField] private List<PendingEntry> _pending = new List<PendingEntry>();
        [SerializeField] private string _selectedPresetId;
        [SerializeField] private Vector2 _scroll;

        // Дефайны записаны, но перекомпиляция ещё не прошла. Снимается в
        // OnEnable, потому что OnEnable вызывается как раз после перезагрузки
        // домена — той самой, которой мы и ждали.
        [SerializeField] private bool _awaitingRecompile;

        private SavedProjectOptionState _saved;
        private Dictionary<string, string> _pendingMap;
        private StagedProjectOptionState _staged;
        private Dictionary<string, ProjectOptionAvailability> _availability;
        private List<ProjectWarning> _warnings;
        private bool _stateValid;
        private bool _proSkin;

        [Serializable]
        private struct PendingEntry
        {
            public string Id;
            public string Value;
        }

        private void OnEnable()
        {
            if (_awaitingRecompile)
                _awaitingRecompile = false;

            _proSkin = EditorGUIUtility.isProSkin;
            _stateValid = false;
        }

        private void OnFocus() => _stateValid = false;

        private void OnProjectChange() => _stateValid = false;

        // Закрытие окна с неприменёнными правками спрашивает — ровно как у
        // изменённого ассета.
        public override void SaveChanges()
        {
            base.SaveChanges();
            Apply();
        }

        public override void DiscardChanges()
        {
            base.DiscardChanges();
            Revert();
        }

        private bool IsDirty => _staged != null && _staged.IsDirty;

        private void OnGUI()
        {
            // Пересобираем состояние только в фазе раскладки. В перерисовке
            // этого делать нельзя: описатели читают ассеты и файлы с диска —
            // ремонт asmdef-ов, например, читает два файла.
            if (Event.current.type == EventType.Layout)
                EnsureState();

            if (_staged == null)
                return;

            if (_proSkin != EditorGUIUtility.isProSkin)
            {
                _proSkin = EditorGUIUtility.isProSkin;
                BuildManagerStyles.Invalidate();
            }

            DrawToolbar();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawPresets();

            foreach (ProjectOptionSection section in ProjectOptionRegistry.Sections)
                DrawSection(section);

            if (ProjectOptionRegistry.Sections.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No package registered any build options. A package declares them by " +
                    "implementing IProjectOptionSectionProvider from LongLeap.Core.Editor.",
                    MessageType.Info);
            }

            DrawWarnings();

            EditorGUILayout.EndScrollView();

            // Подвал ВНЕ прокрутки: Apply и Build должны быть под рукой при
            // любом положении скролла, а причина, по которой кнопка погашена,
            // должна быть видна рядом с самой кнопкой.
            DrawFooter();
        }

        // ---------- состояние ----------

        private void EnsureState()
        {
            if (_stateValid)
                return;

            _saved = new SavedProjectOptionState();
            _pendingMap = new Dictionary<string, string>();

            foreach (PendingEntry entry in _pending)
            {
                if (!string.IsNullOrEmpty(entry.Id))
                    _pendingMap[entry.Id] = entry.Value;
            }

            _staged = new StagedProjectOptionState(_saved, _pendingMap);

            PrunePending();
            RefreshDerived();

            if (string.IsNullOrEmpty(_selectedPresetId))
                _selectedPresetId = InferPreset();

            _stateValid = true;
        }

        // Отложенные правки могли устареть, пока окно было не в фокусе: кто-то
        // переключил тот же дефайн из меню, или настройка стала недоступна.
        private void PrunePending()
        {
            foreach (string id in _pendingMap.Keys.ToList())
            {
                if (!ProjectOptionRegistry.TryGetOption(id, out ProjectOption option))
                {
                    _pendingMap.Remove(id);
                    continue;
                }

                // Совпало с сохранённым — правка перестала быть правкой.
                if (_pendingMap[id] == _saved.GetValue(id))
                {
                    _pendingMap.Remove(id);
                    continue;
                }

                if (SafeAvailability(option).IsAvailable)
                    continue;

                Debug.LogWarning(
                    $"{Title}: dropping the pending change to '{option.Label}' — " +
                    "it is no longer available.");

                _pendingMap.Remove(id);
            }

            SyncPendingList();
        }

        private void SyncPendingList()
        {
            _pending.Clear();

            foreach (KeyValuePair<string, string> entry in _pendingMap)
                _pending.Add(new PendingEntry { Id = entry.Key, Value = entry.Value });
        }

        // Доступность и замечания считаются здесь, а НЕ в перерисовке: предикат
        // доступности у GAMEPIX делает Directory.Exists, а поставщики замечаний
        // читают ассеты. На каждый кадр это было бы расточительно, а меняться
        // они могут только от правки или от пересбора состояния.
        private void RefreshDerived()
        {
            _availability = new Dictionary<string, ProjectOptionAvailability>();

            foreach (ProjectOption option in ProjectOptionRegistry.AllOptions())
                _availability[option.Id] = SafeAvailability(option);

            _warnings = ProjectWarnings.Collect(_staged);

            hasUnsavedChanges = IsDirty;
            saveChangesMessage =
                "You have staged build options that have not been applied.";

            titleContent = new GUIContent(IsDirty ? DirtyTitle : Title);
        }

        private ProjectOptionAvailability SafeAvailability(ProjectOption option)
        {
            try
            {
                ProjectOptionAvailability availability = option.GetAvailability(_staged);

                // Односторонняя настройка, которая уже сделана, — это как
                // погасший пункт ремонта asmdef-ов: чинить больше нечего.
                // Смотрим на СОХРАНЁННОЕ значение, как и меню: по отложенному
                // строка гасла сразу после щелчка, а PrunePending в Apply
                // выбрасывал правку как «недоступную» — ремонт не делался.
                if (availability.IsAvailable
                    && option.OneWay
                    && _staged.GetSavedValue(option.Id) == ProjectOption.On)
                {
                    return ProjectOptionAvailability.Blocked("Already in place — nothing to do.");
                }

                return availability;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"{Title}: the availability check for '{option.Id}' threw and the option " +
                    $"was disabled. {exception}");

                return ProjectOptionAvailability.Blocked("Its availability check threw — see the Console.");
            }
        }

        private ProjectOptionAvailability AvailabilityOf(ProjectOption option) =>
            _availability != null && _availability.TryGetValue(option.Id, out ProjectOptionAvailability found)
                ? found
                : ProjectOptionAvailability.Available;

        private void SetOption(ProjectOption option, string value)
        {
            _staged.Set(option.Id, value);

            // После правки часть настроек могла стать недоступной — их
            // отложенные значения надо вернуть к сохранённым, иначе строка
            // покажется изменённой, а Apply полез бы её писать.
            ProjectOptionPresets.Reconcile(_staged);

            SyncPendingList();
            RefreshDerived();
        }

        private string InferPreset()
        {
            foreach (ProjectOptionPreset preset in ProjectOptionPresets.All)
            {
                if (ProjectOptionPresets.Matches(preset, _staged))
                    return preset.Id;
            }

            return null;
        }

        // ---------- отрисовка ----------

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(DescribePreset(), EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton))
                    _stateValid = false;
            }
        }

        private string DescribePreset()
        {
            if (string.IsNullOrEmpty(_selectedPresetId))
                return "Preset: Custom";

            ProjectOptionPreset preset =
                ProjectOptionPresets.All.FirstOrDefault(p => p.Id == _selectedPresetId);

            if (preset == null)
                return "Preset: Custom";

            return ProjectOptionPresets.Matches(preset, _staged)
                ? $"Preset: {preset.Label}"
                : $"Preset: {preset.Label} (modified)";
        }

        private void DrawPresets()
        {
            IReadOnlyList<ProjectOptionPreset> presets = ProjectOptionPresets.All;

            if (presets.Count == 0)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("Preset", BuildManagerStyles.SectionHeader);

                using (new EditorGUILayout.HorizontalScope())
                {
                    foreach (ProjectOptionPreset preset in presets)
                    {
                        bool selected = preset.Id == _selectedPresetId;

                        GUIStyle style = selected
                            ? BuildManagerStyles.PresetSelected
                            : EditorStyles.miniButton;

                        if (GUILayout.Button(new GUIContent(preset.Label, preset.Tooltip), style))
                            ApplyPreset(preset);
                    }
                }
            }
        }

        private void ApplyPreset(ProjectOptionPreset preset)
        {
            ProjectOptionPresets.Stage(preset, _staged);

            _selectedPresetId = preset.Id;

            SyncPendingList();
            RefreshDerived();
        }

        private void DrawSection(ProjectOptionSection section)
        {
            List<ProjectOption> visible = section.Options
                .Where(o => o.VisibleInWindow)
                .ToList();

            if (visible.Count == 0 && section.Actions.Count == 0)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                bool sectionDirty = visible.Any(o => _staged.IsOptionDirty(o.Id));

                GUILayout.Label(
                    sectionDirty ? $"* {section.Title}" : section.Title,
                    BuildManagerStyles.SectionHeader);

                if (!string.IsNullOrEmpty(section.Description))
                    GUILayout.Label(section.Description, EditorStyles.miniLabel);

                List<ProjectOption> required = visible.Where(o => o.Required).ToList();
                List<ProjectOption> optional = visible.Where(o => !o.Required).ToList();

                // Подзаголовки рисуем только когда есть обе половины. Иначе
                // одинокий «Optional» над единственной строкой DevTools —
                // просто шум.
                bool split = required.Count > 0 && optional.Count > 0;

                DrawOptionGroup(split ? "Required" : null, required);
                DrawOptionGroup(split ? "Optional" : null, optional);

                DrawSectionActions(section);
            }
        }

        private void DrawOptionGroup(string header, List<ProjectOption> options)
        {
            if (options.Count == 0)
                return;

            if (!string.IsNullOrEmpty(header))
                GUILayout.Label(header, EditorStyles.miniBoldLabel);

            foreach (ProjectOption option in options)
                DrawOption(option);
        }

        private void DrawOption(ProjectOption option)
        {
            string effective = _staged.GetValue(option.Id);
            bool dirty = _staged.IsOptionDirty(option.Id);
            ProjectOptionAvailability availability = AvailabilityOf(option);

            float lineHeight = EditorGUIUtility.singleLineHeight;

            Rect row = GUILayoutUtility.GetRect(
                0f, lineHeight + BuildManagerStyles.RowPadding * 2f);

            // Фон рисуем ДО контрола и по заранее полученному прямоугольнику:
            // GUILayoutUtility.GetLastRect после EditorGUILayout отстаёт на
            // кадр, и подсветка мигала бы.
            if (dirty)
                BuildManagerStyles.DrawDirtyBackground(row);

            float markerX = row.x + BuildManagerStyles.AccentBarWidth + 2f;

            if (dirty)
            {
                GUI.Label(
                    new Rect(markerX, row.y + BuildManagerStyles.RowPadding,
                        BuildManagerStyles.MarkerWidth, lineHeight),
                    "*",
                    BuildManagerStyles.Marker);
            }

            float controlX = markerX + BuildManagerStyles.MarkerWidth;

            var control = new Rect(
                controlX,
                row.y + BuildManagerStyles.RowPadding,
                Mathf.Max(80f, row.xMax - controlX - 4f),
                lineHeight);

            var content = new GUIContent(
                BuildLabel(option, effective, dirty),
                BuildTooltip(option, availability, dirty));

            using (new EditorGUI.DisabledScope(!availability.IsAvailable))
            {
                EditorGUI.BeginChangeCheck();

                if (option.Choices == null)
                {
                    bool on = EditorGUI.ToggleLeft(
                        control,
                        content,
                        effective == ProjectOption.On,
                        dirty ? BuildManagerStyles.DirtyLabel : EditorStyles.label);

                    if (EditorGUI.EndChangeCheck())
                        SetOption(option, on ? ProjectOption.On : ProjectOption.Off);
                }
                else
                {
                    IReadOnlyList<ProjectOptionChoice> choices = option.Choices;

                    var labels = new GUIContent[choices.Count];
                    int index = -1;

                    for (int i = 0; i < choices.Count; i++)
                    {
                        labels[i] = new GUIContent(choices[i].Label, choices[i].Tooltip);

                        if (choices[i].Value == effective)
                            index = i;
                    }

                    int picked = EditorGUI.Popup(control, content, index, labels);

                    if (EditorGUI.EndChangeCheck() && picked >= 0 && picked < choices.Count)
                        SetOption(option, choices[picked].Value);
                }
            }

            if (!availability.IsAvailable)
                GUILayout.Label(availability.Reason, BuildManagerStyles.Reason);
        }

        // Приписка говорит, ЧТО именно изменится, а не только что что-то
        // изменилось: это тот сигнал подсветки, который работает и в оттенках
        // серого, и при любом масштабе.
        private string BuildLabel(ProjectOption option, string effective, bool dirty)
        {
            if (!dirty)
                return option.Label;

            string from = option.DescribeValue(_staged.GetSavedValue(option.Id));
            string to = option.DescribeValue(effective);

            return $"{option.Label}   ({from} -> {to})";
        }

        private static string BuildTooltip(
            ProjectOption option, ProjectOptionAvailability availability, bool dirty)
        {
            string tooltip = option.Tooltip ?? string.Empty;

            if (dirty)
                tooltip = Append(tooltip, "Pending change — not applied yet.");

            if (!availability.IsAvailable)
                tooltip = Append(tooltip, availability.Reason);

            return tooltip;
        }

        private static string Append(string text, string line) =>
            string.IsNullOrEmpty(text) ? line : text + "\n\n" + line;

        private void DrawSectionActions(ProjectOptionSection section)
        {
            if (section.Actions.Count == 0)
                return;

            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (ProjectOptionAction action in section.Actions)
                {
                    string disabled = null;

                    try
                    {
                        disabled = action.GetDisabledReason();
                    }
                    catch (Exception exception)
                    {
                        Debug.LogError(
                            $"{Title}: the disabled-reason check for action '{action.Label}' " +
                            $"threw. {exception}");
                    }

                    string tooltip = disabled ?? action.Tooltip;

                    using (new EditorGUI.DisabledScope(disabled != null))
                    {
                        if (GUILayout.Button(
                                new GUIContent(action.Label, tooltip),
                                EditorStyles.miniButton))
                        {
                            ProjectOptionAction invoked = action;
                            EditorApplication.delayCall += () => RunAction(invoked);
                        }
                    }
                }

                GUILayout.FlexibleSpace();
            }
        }

        private void RunAction(ProjectOptionAction action)
        {
            try
            {
                action.Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogError($"{Title}: action '{action.Label}' failed. {exception}");
            }

            _stateValid = false;
            Repaint();
        }

        private void DrawWarnings()
        {
            if (_warnings == null || _warnings.Count == 0)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("Before you build", BuildManagerStyles.SectionHeader);

                foreach (ProjectWarning warning in _warnings)
                {
                    EditorGUILayout.HelpBox(warning.Message, ToMessageType(warning.Severity));

                    if (!warning.HasFix)
                        continue;

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();

                        if (GUILayout.Button(warning.FixLabel, EditorStyles.miniButton))
                        {
                            ProjectWarning invoked = warning;
                            EditorApplication.delayCall += () => RunFix(invoked);
                        }
                    }
                }
            }
        }

        private void RunFix(ProjectWarning warning)
        {
            try
            {
                warning.Fix();
            }
            catch (Exception exception)
            {
                Debug.LogError($"{Title}: the fix for '{warning.Message}' failed. {exception}");
            }

            _stateValid = false;
            Repaint();
        }

        private static MessageType ToMessageType(ProjectWarningSeverity severity)
        {
            switch (severity)
            {
                case ProjectWarningSeverity.Error: return MessageType.Error;
                case ProjectWarningSeverity.Warning: return MessageType.Warning;
                default: return MessageType.Info;
            }
        }

        // ---------- подвал ----------

        private void DrawFooter()
        {
            string buildBlocked = BuildDisabledReason();
            bool busy = EditorApplication.isCompiling
                || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!IsDirty || busy))
                    {
                        if (GUILayout.Button(
                                new GUIContent(
                                    IsDirty ? $"Apply ({_pendingMap.Count})" : "Apply",
                                    "Write the staged options: one define write, one recompile."),
                                GUILayout.Height(24f)))
                        {
                            EditorApplication.delayCall += Apply;
                        }
                    }

                    using (new EditorGUI.DisabledScope(!IsDirty))
                    {
                        if (GUILayout.Button(
                                new GUIContent("Revert", "Drop the staged options and re-read the project."),
                                GUILayout.Height(24f)))
                        {
                            EditorApplication.delayCall += Revert;
                        }
                    }

                    using (new EditorGUI.DisabledScope(buildBlocked != null))
                    {
                        if (GUILayout.Button(
                                new GUIContent(BuildLabelText(), "Build with the options as applied."),
                                GUILayout.Height(24f)))
                        {
                            EditorApplication.delayCall += Build;
                        }
                    }
                }

                if (buildBlocked != null)
                    EditorGUILayout.HelpBox(buildBlocked, MessageType.Info);
            }
        }

        // Подпись считаем по ОТЛОЖЕННОМУ состоянию, чтобы было видно, чем
        // обернётся правка, ещё до Apply. Собирает при этом всегда применённая
        // конфигурация — собрать из грязного состояния всё равно нельзя.
        private string BuildLabelText()
        {
            ProjectBuildTarget target = ProjectBuildTargets.Select(_staged);

            return target == null ? "Build" : $"Build ({target.Label})";
        }

        private string BuildDisabledReason()
        {
            if (IsDirty)
                return $"Apply first — {_pendingMap.Count} staged change(s). " +
                       "Defines need a recompile before they reach the build.";

            if (_awaitingRecompile)
                return "Waiting for script recompilation after Apply.";

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return "Wait for script compilation and asset import to finish.";

            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return "Exit Play Mode first.";

            if (!EditorBuildSettings.scenes.Any(scene => scene.enabled))
                return "Enable at least one scene in File / Build Profiles.";

            if (_warnings != null)
            {
                foreach (ProjectWarning warning in _warnings)
                {
                    if (warning.BlocksBuild)
                        return warning.Message;
                }
            }

            ProjectBuildTarget target = ProjectBuildTargets.Select(_saved);

            if (target == null)
                return "No package registered a build target.";

            ProjectBuildTargetAvailability availability = target.GetAvailability();

            return availability.IsAvailable ? null : availability.Reason;
        }

        // ---------- действия ----------

        private void Apply()
        {
            // Через delayCall, а не из OnGUI: запись дефайнов ставит в очередь
            // перезагрузку домена, а DisplayDialog прокручивает цикл событий —
            // и то и другое внутри блока IMGUI даёт «You are pushing more
            // GUIClips than you are popping».
            _stateValid = false;
            EnsureState();

            if (!IsDirty)
                return;

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorUtility.DisplayDialog(
                    Title,
                    "The editor is compiling scripts or importing assets. Wait for it to finish " +
                    "and press Apply again.",
                    "OK");
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog(
                    Title,
                    "Applying build options reloads the script domain, which would tear down the " +
                    "running game. Exit Play Mode first.",
                    "OK");
                return;
            }

            var changes = new List<Change>();
            var cancelled = new List<string>();

            foreach (string id in _pendingMap.Keys.ToList())
            {
                if (!ProjectOptionRegistry.TryGetOption(id, out ProjectOption option))
                    continue;

                string to = _pendingMap[id];
                ProjectOptionConfirmation confirmation = option.GetConfirmation(to);

                if (confirmation != null && !EditorUtility.DisplayDialog(
                        confirmation.Title,
                        confirmation.Message,
                        confirmation.Confirm,
                        confirmation.Cancel))
                {
                    // Отменили одно подтверждение — теряем одну правку, а не
                    // весь Apply.
                    _pendingMap.Remove(id);
                    cancelled.Add(option.Label);
                    continue;
                }

                changes.Add(new Change(option, _saved.GetValue(id), to));
            }

            if (changes.Count > 0)
            {
                var context = new ProjectWriteContext();

                // Порядок важен: сначала произвольные записи (они правят файлы
                // на диске), потом ассеты, и только потом дефайны — компиляция,
                // которую запустит запись дефайна, должна увидеть уже
                // исправленные файлы.
                foreach (Change change in changes.Where(c => c.Option is CustomOption))
                    change.Option.Write(change.To, context);

                foreach (Change change in changes.Where(c => c.Option is AssetEnumOption))
                    change.Option.Write(change.To, context);

                foreach (Change change in changes.Where(c => c.Option is DefineOption))
                    change.Option.Write(change.To, context);

                context.Log(Summary(changes));

                foreach (Change change in changes)
                    context.Log(change.Option.DescribeApplied(change.To));

                ProjectWriteResult result = context.Flush();

                _awaitingRecompile = result.DefinesChanged;
            }

            if (cancelled.Count > 0)
            {
                Debug.Log(
                    $"{Title}: not applied because the confirmation was declined — " +
                    string.Join(", ", cancelled) + ".");
            }

            _pendingMap.Clear();
            SyncPendingList();

            _stateValid = false;
            Repaint();
        }

        private static string Summary(List<Change> changes)
        {
            var text = new System.Text.StringBuilder();

            text.Append($"{Title} applied {changes.Count} change(s):");

            foreach (Change change in changes)
            {
                text.AppendLine();
                text.Append("  ")
                    .Append(change.Option.Label)
                    .Append(": ")
                    .Append(change.Option.DescribeValue(change.From))
                    .Append(" -> ")
                    .Append(change.Option.DescribeValue(change.To));
            }

            return text.ToString();
        }

        private void Revert()
        {
            // Перечитываем, а не просто чистим: проект мог уехать с тех пор,
            // как окно открыли, и «вернуть как в проекте» значит именно это.
            _pendingMap?.Clear();
            _pending.Clear();
            _selectedPresetId = null;

            _stateValid = false;
            EnsureState();

            Repaint();
        }

        private void Build()
        {
            _stateValid = false;
            EnsureState();

            string blocked = BuildDisabledReason();

            if (blocked != null)
            {
                Debug.LogWarning($"{Title}: not building — {blocked}");
                return;
            }

            ProjectBuildTarget target = ProjectBuildTargets.Select(_saved);

            if (target == null)
                return;

            try
            {
                target.Build();
            }
            catch (Exception exception)
            {
                Debug.LogError($"{Title}: build target '{target.Id}' failed. {exception}");
            }

            _stateValid = false;
            Repaint();
        }

        private readonly struct Change
        {
            public ProjectOption Option { get; }

            public string From { get; }

            public string To { get; }

            public Change(ProjectOption option, string from, string to)
            {
                Option = option;
                From = from;
                To = to;
            }
        }
    }
}
