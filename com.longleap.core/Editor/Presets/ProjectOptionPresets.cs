using System;
using System.Collections.Generic;
using UnityEngine;

namespace LongLeap.Core.Editor
{
    public static class ProjectOptionPresets
    {
        // Больше восьми проходов согласования означает, что кто-то написал
        // circular-предикат доступности. Это ошибка описателя, и молчать о ней
        // нельзя.
        private const int MaxReconcilePasses = 8;

        private static IReadOnlyList<ProjectOptionPreset> _presets;

        public static IReadOnlyList<ProjectOptionPreset> All => _presets ?? (_presets = Build());

        public static void Invalidate() => _presets = null;

        // Разложить пресет по отложенному состоянию.
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

                // Не ошибка: в проекте без этого пакета так и должно быть.
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

        // Уводим недоступные настройки назад к сохранённому значению, пока
        // состояние не перестанет меняться.
        //
        // Проход нужен не один, и это не перестраховка. Пример из текущего
        // состояния проекта, где нет папки Assets/Plugins/GamePix: пресет
        // GamePix ставит GAMEPIX включённым, но настройка недоступна -> её
        // возвращаем к сохранённому «выключено» -> и ТОЛЬКО ТЕПЕРЬ становится
        // недоступен GAMEPIX_ALLOW_ANALYTICS, которому нужен GAMEPIX. За один
        // проход он остался бы включённым без GAMEPIX за ним.
        public static void Reconcile(IStagedOptionState staged)
        {
            if (staged == null)
                return;

            for (int pass = 0; pass < MaxReconcilePasses; pass++)
            {
                bool changed = false;

                foreach (ProjectOption option in ProjectOptionRegistry.AllOptions())
                {
                    if (option.GetAvailability(staged).IsAvailable)
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

        // Совпадает ли состояние с пресетом. Недоступные настройки не
        // учитываем: согласование оставило их как были, и требовать от них
        // значения пресета значило бы навсегда показывать «(modified)».
        public static bool Matches(ProjectOptionPreset preset, IProjectOptionState state)
        {
            if (preset == null || state == null)
                return false;

            foreach ((ProjectOption option, string target) in Targets(preset))
            {
                if (!option.GetAvailability(state).IsAvailable)
                    continue;

                if (state.GetValue(option.Id) != target)
                    return false;
            }

            return true;
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
