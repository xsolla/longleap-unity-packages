using System;
using System.Collections.Generic;
using UnityEngine;

namespace LongLeap.Core.Editor
{
    public static class ProjectBuildTargets
    {
        private static IReadOnlyList<ProjectBuildTarget> _targets;

        public static IReadOnlyList<ProjectBuildTarget> All => _targets ?? (_targets = Build());

        public static void Invalidate() => _targets = null;

        // Кто соберёт игру при текущей конфигурации. Побеждает наибольший
        // Priority среди подходящих, поэтому вендорный билдер площадки
        // (Priority 100) перебивает обычный WebGL (Priority 0), когда включён
        // GAMEPIX.
        public static ProjectBuildTarget Select(IProjectOptionState state)
        {
            ProjectBuildTarget best = null;

            foreach (ProjectBuildTarget target in All)
            {
                bool applicable;

                try
                {
                    applicable = target.IsApplicable(state);
                }
                catch (Exception exception)
                {
                    Debug.LogError(
                        $"Build options: build target '{target.Id}' failed its applicability " +
                        $"check and was skipped. {exception}");
                    continue;
                }

                if (!applicable)
                    continue;

                if (best == null || target.Priority > best.Priority)
                    best = target;
            }

            return best;
        }

        private static IReadOnlyList<ProjectBuildTarget> Build()
        {
            List<(ProjectBuildTarget Target, string Provider)> found =
                ProviderDiscovery.Collect<IProjectBuildTargetProvider, ProjectBuildTarget>(
                    provider => provider.GetBuildTargets());

            var ids = new Dictionary<string, string>();
            var result = new List<ProjectBuildTarget>();

            foreach ((ProjectBuildTarget target, string provider) in found)
            {
                if (ids.TryGetValue(target.Id, out string owner))
                {
                    Debug.LogError(
                        $"Build options: build target id '{target.Id}' is declared by both {owner} " +
                        $"and {provider}. Keeping the first.");
                    continue;
                }

                ids[target.Id] = provider;
                result.Add(target);
            }

            return result;
        }
    }
}
