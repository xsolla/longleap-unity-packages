using System;
using System.Collections.Generic;
using System.Linq;
using LongLeap.BuildManager.Options;
using UnityEditor;
using UnityEngine;

namespace LongLeap.BuildManager.Editor
{
    // A window that shows the whole build configuration at once.
    //
    // The key property: clicking a checkbox writes NOTHING. Previously every
    // menu item changed the scripting defines immediately, so switching the
    // distribution cost half a dozen clicks with a recompile between each one.
    // Here edits accumulate, changed rows are highlighted, and Apply writes
    // everything in one go: one define write, one domain reload.
    //
    // The game can only be built from a CLEAN state, and that is not a
    // formality: until the edits are applied and recompiled, #if-gated code
    // compiles against the old defines. For example, a build post-process step
    // that lives entirely under an #if would simply not run in such a build.
    internal sealed class BuildManagerWindow : EditorWindow
    {
        private const string Title = "Build Manager";
        private const string DirtyTitle = "Build Manager*";

        // Priority 0 keeps the window at the top of the LongLeap menu. Unity
        // draws a separator before any item whose priority is more than ten
        // higher, so other packages can group their items below it.
        [MenuItem("LongLeap/" + Title, false, 0)]
        private static void Open()
        {
            BuildManagerWindow window = GetWindow<BuildManagerWindow>();
            window.titleContent = new GUIContent(Title);
            window.minSize = new Vector2(460f, 400f);
        }

        // Staged edits ARE serialized. This is not continue-after-domain-reload
        // machinery, just ordinary EditorWindow field serialization, the same
        // that remembers expanded foldouts in the inspector. Without it, saving
        // any unrelated script would silently throw away the prepared
        // configuration.
        [SerializeField] private List<PendingEntry> _pending = new List<PendingEntry>();
        [SerializeField] private string _selectedPresetId;
        [SerializeField] private Vector2 _scroll;

        // Defines are written but the recompile has not happened yet. Cleared
        // in OnEnable, which runs right after the domain reload we were
        // waiting for. A compile that fails does not reload the domain, so the
        // flag stays set; BuildDisabledReason reports the compile errors
        // before it, and the reload after the errors are fixed clears it.
        // (CompilationPipeline.compilationFinished is not used on purpose: it
        // fires before the reload, while the old assemblies are still loaded.)
        [SerializeField] private bool _awaitingRecompile;

        // The version the next Build writes to PlayerSettings.bundleVersion,
        // and the current version it was suggested for. When the current
        // version moves (a Build saved it, or someone edited Player Settings)
        // the new one is suggested again; otherwise the typed text is kept,
        // even while it is invalid, so a half-typed value is not overwritten.
        [SerializeField] private string _newVersion;
        [SerializeField] private string _versionSeen;

        private string _currentVersion;

        private SavedProjectOptionState _saved;
        private Dictionary<string, string> _pendingMap;
        private StagedProjectOptionState _staged;
        private Dictionary<string, ProjectOptionAvailability> _availability;
        private List<ProjectWarning> _warnings;
        private bool _stateValid;
        private bool _proSkin;

        // Computed in RefreshDerived, read while drawing. Each of these calls
        // into other packages' predicates, which may touch the disk, so none of
        // them may run per IMGUI event.
        private string _presetLabel;
        private string _buildLabel;
        private string _buildBlockedReason;
        private Dictionary<ProjectOptionAction, string> _actionDisabledReasons;

        [Serializable]
        private struct PendingEntry
        {
            public string Id;
            public string Value;
        }

        private void OnEnable()
        {
            _awaitingRecompile = false;
            _proSkin = EditorGUIUtility.isProSkin;
            _stateValid = false;
        }

        private void OnFocus() => _stateValid = false;

        private void OnProjectChange() => _stateValid = false;

        // Closing the window with unapplied edits asks first, exactly like a
        // modified asset.
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
            // Rebuild the state only during the Layout pass. It must not be done
            // during Repaint: option descriptors may read assets and files
            // from disk.
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
                    "implementing IProjectOptionSectionProvider from LongLeap.BuildManager.Options.",
                    MessageType.Info);
            }

            // After every option section, so it reads as the last step before
            // Build.
            DrawVersion();

            DrawWarnings();

            EditorGUILayout.EndScrollView();

            // The footer is OUTSIDE the scroll view: Apply and Build must be at
            // hand at any scroll position, and the reason a button is disabled
            // must be visible next to the button itself.
            DrawFooter();
        }

        // ---------- state ----------

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

            // Before RefreshDerived, which builds the toolbar label from it.
            if (string.IsNullOrEmpty(_selectedPresetId))
                _selectedPresetId = InferPreset();

            SyncVersion();

            RefreshDerived();

            _stateValid = true;
        }

        // Staged edits may have gone stale while the window was out of focus:
        // someone toggled the same define from the menu, or the option became
        // unavailable.
        private void PrunePending()
        {
            foreach (string id in _pendingMap.Keys.ToList())
            {
                if (!ProjectOptionRegistry.TryGetOption(id, out ProjectOption option))
                {
                    _pendingMap.Remove(id);
                    continue;
                }

                // Matches the saved value, so the edit is no longer an edit.
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

        // Availability and warnings are computed here, NOT during repaint:
        // availability predicates may touch the disk (Directory.Exists, for
        // example), and warning providers read assets. Doing that every frame
        // would be wasteful, and they can only change after an edit or a state
        // rebuild.
        private void RefreshDerived()
        {
            _availability = new Dictionary<string, ProjectOptionAvailability>();

            foreach (ProjectOption option in ProjectOptionRegistry.AllOptions())
                _availability[option.Id] = SafeAvailability(option);

            _warnings = ProjectWarnings.Collect(_staged);

            _presetLabel = DescribePreset();
            _buildLabel = BuildLabelText();
            _buildBlockedReason = ProjectBuildBlockedReason();
            _actionDisabledReasons = CollectActionDisabledReasons();

            hasUnsavedChanges = IsDirty;
            saveChangesMessage =
                "You have staged build options that have not been applied.";

            titleContent = new GUIContent(IsDirty ? DirtyTitle : Title);
        }

        private void SyncVersion()
        {
            _currentVersion = PlayerSettings.bundleVersion ?? string.Empty;

            if (_versionSeen == _currentVersion && _newVersion != null)
                return;

            _versionSeen = _currentVersion;
            _newVersion = BundleVersion.Increment(_currentVersion);

            // A focused text field keeps showing its own edit buffer and would
            // hide the new suggestion.
            GUIUtility.keyboardControl = 0;
        }

        private ProjectOptionAvailability SafeAvailability(ProjectOption option)
        {
            try
            {
                ProjectOptionAvailability availability = option.GetAvailability(_staged);

                // A one-way option (a one-time repair, for example) that is
                // already done has nothing left to do, so it is disabled.
                // Check the SAVED value, as the menu does: checking the staged
                // value would disable the row right after the click, and
                // PrunePending in Apply would then drop the edit as
                // "unavailable", so the option would never be written.
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

            // After an edit some options may have become unavailable. Their
            // staged values must go back to the saved ones, otherwise the row
            // would look changed and Apply would try to write it.
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

        // ---------- drawing ----------

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(_presetLabel, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Settings", EditorStyles.toolbarButton))
                    SettingsService.OpenProjectSettings(BuildManagerSettings.SettingsPath);

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

                // Draw the subheaders only when both halves exist. Otherwise a
                // lone "Optional" above a section's only row is just noise.
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

            // Draw the background BEFORE the control, using a rect reserved up
            // front: GUILayoutUtility.GetLastRect after EditorGUILayout lags by
            // one frame, and the highlight would flicker.
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

        // The suffix says WHAT will change, not just that something changed:
        // this is the highlight signal that works both in greyscale and at any
        // scale.
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
                    _actionDisabledReasons?.TryGetValue(action, out disabled);

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

        private static Dictionary<ProjectOptionAction, string> CollectActionDisabledReasons()
        {
            var reasons = new Dictionary<ProjectOptionAction, string>();

            foreach (ProjectOptionSection section in ProjectOptionRegistry.Sections)
            {
                foreach (ProjectOptionAction action in section.Actions)
                {
                    try
                    {
                        reasons[action] = action.GetDisabledReason();
                    }
                    catch (Exception exception)
                    {
                        Debug.LogError(
                            $"{Title}: the disabled-reason check for action '{action.Label}' " +
                            $"threw and the action was disabled. {exception}");

                        reasons[action] = "Its disabled-reason check threw — see the Console.";
                    }
                }
            }

            return reasons;
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

        // ---------- footer ----------

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
                                new GUIContent(
                                    _buildLabel,
                                    "Save the new version to Player Settings, then build with the " +
                                    "options as applied."),
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

        // Current version (locked) -> new version (editable). The new version
        // is written by Build, not by Apply: it needs no recompile, and every
        // build gets its own number, failed builds included.
        private void DrawVersion()
        {
            const string tooltip =
                "The left value is the current PlayerSettings.bundleVersion (Application.version). " +
                "Build saves the right value there before building. It must be higher.";

            bool busy = EditorApplication.isCompiling
                || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(new GUIContent("Version", tooltip), GUILayout.Width(52f));

                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.TextField(_currentVersion, GUILayout.MinWidth(60f));

                GUILayout.Label("->", GUILayout.Width(20f));

                using (new EditorGUI.DisabledScope(busy))
                    _newVersion = EditorGUILayout.TextField(_newVersion, GUILayout.MinWidth(60f));
            }
        }

        private string VersionProblem() =>
            BundleVersion.Validate(_currentVersion, _newVersion?.Trim());

        // The label is computed from the STAGED state, so you can see what an
        // edit will lead to before Apply. The build itself always uses the
        // applied configuration: building from a dirty state is not allowed
        // anyway.
        private string BuildLabelText()
        {
            ProjectBuildTarget target = ProjectBuildTargets.Select(_staged);

            return target == null ? "Build" : $"Build ({target.Label})";
        }

        // Editor-state checks are cheap and change without any edit, so they
        // run live. Everything that depends on the project comes from the
        // reason RefreshDerived cached.
        private string BuildDisabledReason()
        {
            if (IsDirty)
                return $"Apply first — {_pendingMap.Count} staged change(s). " +
                       "Defines need a recompile before they reach the build.";

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return "Wait for script compilation and asset import to finish.";

            // Before _awaitingRecompile: a failed compile does not reload the
            // domain, so the flag stays set and "waiting" would be wrong.
            if (EditorUtility.scriptCompilationFailed)
                return "Fix the script compilation errors first (see the Console).";

            if (_awaitingRecompile)
                return "Waiting for script recompilation after Apply.";

            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return "Exit Play Mode first.";

            // Checked live: the field changes on every keystroke, and the
            // check is a cheap string parse.
            return _buildBlockedReason ?? VersionProblem();
        }

        private string ProjectBuildBlockedReason()
        {
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

            try
            {
                ProjectBuildTargetAvailability availability = target.GetAvailability();

                return availability.IsAvailable ? null : availability.Reason;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"{Title}: the availability check for build target '{target.Id}' threw. {exception}");

                return $"The availability check for build target '{target.Id}' threw — see the Console.";
            }
        }

        // ---------- actions ----------

        private void Apply()
        {
            // Called through delayCall, not from OnGUI: writing defines queues
            // a domain reload, and DisplayDialog pumps the event loop. Either
            // one inside an IMGUI block causes "You are pushing more GUIClips
            // than you are popping".
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
                    // Declining one confirmation drops one edit, not the whole
                    // Apply.
                    //
                    // Known limit: off-cascades that this edit staged stay
                    // staged. It cannot happen with today's descriptors:
                    // DefineOption asks only when turning ON, and cascades
                    // happen only when turning OFF.
                    _pendingMap.Remove(id);
                    cancelled.Add(option.Label);
                    continue;
                }

                changes.Add(new Change(option, _saved.GetValue(id), to));
            }

            // Edits whose write threw. They stay staged, so the row stays
            // highlighted and Apply can be retried.
            var failed = new List<Change>();

            if (changes.Count > 0)
            {
                var context = new ProjectWriteContext();

                // Order matters: custom writes first (they patch files on
                // disk), then assets, and only then defines. The compile that
                // the define write triggers must see the already-patched files.
                IEnumerable<Change> ordered = changes.Where(c => c.Option is CustomOption)
                    .Concat(changes.Where(c => c.Option is AssetEnumOption))
                    .Concat(changes.Where(c => c.Option is DefineOption));

                var written = new List<Change>();

                foreach (Change change in ordered)
                {
                    try
                    {
                        change.Option.Write(change.To, context);
                        written.Add(change);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogError(
                            $"{Title}: writing '{change.Option.Label}' failed; the change stays " +
                            $"staged. {exception}");

                        failed.Add(change);
                    }
                }

                if (written.Count > 0)
                {
                    context.Log(Summary(written));

                    foreach (Change change in written)
                        context.Log(change.Option.DescribeApplied(change.To));
                }

                try
                {
                    // Asset and define writes are buffered until here; only
                    // custom writes already went to disk.
                    ProjectWriteResult result = context.Flush();

                    _awaitingRecompile = result.DefinesChanged;
                }
                catch (Exception exception)
                {
                    // Nothing buffered is known to have landed, so keep every
                    // change staged. Custom writes are re-run on the next Apply.
                    Debug.LogError(
                        $"{Title}: writing the build options failed; all changes stay staged. " +
                        $"{exception}");

                    failed.AddRange(written);
                }
            }

            if (cancelled.Count > 0)
            {
                Debug.Log(
                    $"{Title}: not applied because the confirmation was declined — " +
                    string.Join(", ", cancelled) + ".");
            }

            _pendingMap.Clear();

            foreach (Change change in failed)
                _pendingMap[change.Option.Id] = change.To;

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
            // Re-read rather than just clear: the project may have moved on
            // since the window was opened, and "revert to the project" means
            // exactly that.
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

            SaveVersion();

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

        // Runs BEFORE the build: Application.version is baked into the player
        // at build time. A failed build keeps the new number, so the next
        // Build suggests the one after it.
        private void SaveVersion()
        {
            string from = PlayerSettings.bundleVersion;
            string to = _newVersion.Trim();

            PlayerSettings.bundleVersion = to;

            // Writes ProjectSettings.asset now, so the version survives an
            // editor crash during the build.
            AssetDatabase.SaveAssets();

            Debug.Log($"{Title}: version {from} -> {to}.");
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
