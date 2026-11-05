using System.Collections.Generic;

namespace LongLeap.Core.Editor
{
    public static class ProjectWarnings
    {
        // Providers are queried EVERY time, without a cache: warnings depend on
        // the staged state, which changes with every click, so a cache would
        // be reset on every click anyway.
        //
        // Do not call from a repaint: providers read assets and files.
        public static List<ProjectWarning> Collect(IProjectOptionState pendingState)
        {
            List<(ProjectWarning Warning, string Provider)> found =
                ProviderDiscovery.Collect<IProjectWarningProvider, ProjectWarning>(
                    provider => provider.GetWarnings(pendingState));

            var result = new List<ProjectWarning>();

            foreach ((ProjectWarning warning, string _) in found)
                result.Add(warning);

            // Build blockers first, then by descending severity.
            result.Sort((left, right) =>
            {
                if (left.BlocksBuild != right.BlocksBuild)
                    return left.BlocksBuild ? -1 : 1;

                return right.Severity.CompareTo(left.Severity);
            });

            return result;
        }
    }
}
