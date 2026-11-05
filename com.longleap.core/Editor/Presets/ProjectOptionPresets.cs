using System;
using System.Collections.Generic;
using UnityEngine;

namespace LongLeap.Core.Editor
{
    public static class ProjectOptionPresets
    {
        // More than eight reconciliation passes means some availability
        // predicate is circular. That is a bug in an option and must be
        // reported.
        private const int MaxReconcilePasses = 8;

        private static IReadOnlyList<ProjectOptionPreset> _presets;

        public static IReadOnlyList<ProjectOptionPreset> All => _presets ?? (_presets = Build());

        public static void Invalidate() => _presets = null;

        // Spreads the preset over the staged state.
        public static void Stage(ProjectOptionPreset preset, IStagedOptionState staged)
        {
            if (preset == null || staged == null)
                return;

            foreach (KeyValuePair<string, string> value in preset.Values)
            {
                if (ProjectOptionRegistry.TryGetOption(value.Key, out ProjectOption _))
                {
                    staged.Set(value.Key, value.Value);
                    continue;
                }

                // Not an error: expected in a project without that package.
                Debug.Log(
                    $"Build options: preset '{preset.Label}' names option '{value.Key}', " +
                    "which no package registered. Skipping it.");
            }

            if (preset.Exhaustive)
            {
                foreach (ProjectOption option in ProjectOptionRegistry.AllOptions())
                {
                    if (preset.Values.ContainsKey(option.Id))
                        continue;

                    if (option.OneWay || option.OffValue == null)
                        continue;

                    staged.Set(option.Id, option.OffValue);
                }
            }

            Reconcile(staged);
        }

        // Reverts unavailable options to their saved values until the state
        // stops changing.
        //
        // One pass is not enough. Example: a portal preset turns the portal
        // define on, but that option is unavailable (the vendor plugin is not
        // installed) -> it is reverted to its saved "off" -> only NOW does a
        // companion option that requires the portal define become unavailable.
        // A single pass would leave it on without the define it depends on.
        public static void Reconcile(IStagedOptionState staged)
        {
            if (staged == null)
                return;

            for (int pass = 0; pass < MaxReconcilePasses; pass++)
            {
                bool changed = false;

                foreach (ProjectOption option in ProjectOptionRegistry.AllOptions())
                {
                    if (IsAvailable(option, staged))
                        continue;

                    string before = staged.GetValue(option.Id);

                    staged.Clear(option.Id);

                    if (staged.GetValue(option.Id) != before)
                        changed = true;
                }

                if (!changed)
                    return;
            }

            Debug.LogError(
                "Build options: option availability did not settle after " +
                $"{MaxReconcilePasses} passes. Some GetAvailability predicate is cyclic.");
        }

        // Whether the state matches the preset. Unavailable options are
        // ignored: reconciliation left them as they were, and requiring the
        // preset value from them would show "(modified)" forever.
        public static bool Matches(ProjectOptionPreset preset, IProjectOptionState state)
        {
            if (preset == null || state == null)
                return false;

            foreach ((ProjectOption option, string target) in Targets(preset))
            {
                if (!IsAvailable(option, state))
                    continue;

                if (state.GetValue(option.Id) != target)
                    return false;
            }

            return true;
        }

        // GetAvailability is code from another package. One that throws must
        // not break every preset click and every redraw, so it counts as
        // unavailable, the same as the Build Manager window treats it:
        // Reconcile reverts the option to its saved value and Matches ignores
        // it.
        private static bool IsAvailable(ProjectOption option, IProjectOptionState state)
        {
            try
            {
                return option.GetAvailability(state).IsAvailable;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"Build options: the availability check for '{option.Id}' threw; the option " +
                    $"is treated as unavailable. {exception}");

                return false;
            }
        }

        private static IEnumerable<(ProjectOption Option, string Target)> Targets(
            ProjectOptionPreset preset)
        {
            foreach (ProjectOption option in ProjectOptionRegistry.AllOptions())
            {
                if (preset.Values.TryGetValue(option.Id, out string named))
                {
                    yield return (option, named);
                    continue;
                }

                if (!preset.Exhaustive || option.OneWay || option.OffValue == null)
                    continue;

                yield return (option, option.OffValue);
            }
        }

        private static IReadOnlyList<ProjectOptionPreset> Build()
        {
            List<(ProjectOptionPreset Preset, string Provider)> found =
                ProviderDiscovery.Collect<IProjectOptionPresetProvider, ProjectOptionPreset>(
                    provider => provider.GetPresets());

            found.Sort((left, right) =>
            {
                int byOrder = left.Preset.Order.CompareTo(right.Preset.Order);
                if (byOrder != 0)
                    return byOrder;

                int byLabel = string.Compare(
                    left.Preset.Label, right.Preset.Label, StringComparison.Ordinal);

                return byLabel != 0
                    ? byLabel
                    : string.Compare(left.Provider, right.Provider, StringComparison.Ordinal);
            });

            var ids = new Dictionary<string, string>();
            var result = new List<ProjectOptionPreset>();

            foreach ((ProjectOptionPreset preset, string provider) in found)
            {
                if (ids.TryGetValue(preset.Id, out string owner))
                {
                    Debug.LogError(
                        $"Build options: preset id '{preset.Id}' is declared by both {owner} " +
                        $"and {provider}. Keeping the first.");
                    continue;
                }

                ids[preset.Id] = provider;
                result.Add(preset);
            }

            return result;
        }
    }
}
