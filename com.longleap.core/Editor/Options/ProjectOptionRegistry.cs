using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LongLeap.Core.Editor
{
    // Collects option tables from all loaded assemblies.
    //
    // Uses TypeCache, NOT static registration from [InitializeOnLoad]. Project
    // assemblies (e.g. Assembly-CSharp-Editor) compile AFTER package
    // assemblies, so with static registration a section would appear or not
    // depending on class initialization order. TypeCache is filled after ALL
    // assemblies load and does not depend on order.
    //
    // Hence the rule: no [InitializeOnLoad] code may query the registry. Query
    // it from window OnEnable, menu validators and button handlers, i.e. code
    // that runs after loading is done.
    //
    // The cache is a static field, so it dies with the domain. That is
    // intended: after recompilation the tables are rebuilt.
    public static class ProjectOptionRegistry
    {
        private static IReadOnlyList<ProjectOptionSection> _sections;
        private static Dictionary<string, ProjectOption> _byId;
        private static Dictionary<string, DefineOption> _bySymbol;

        public static IReadOnlyList<ProjectOptionSection> Sections =>
            _sections ?? (_sections = Build());

        public static void Invalidate()
        {
            _sections = null;
            _byId = null;
            _bySymbol = null;
        }

        public static bool TryGetOption(string optionId, out ProjectOption option)
        {
            EnsureIndex();

            if (string.IsNullOrEmpty(optionId))
            {
                option = null;
                return false;
            }

            return _byId.TryGetValue(optionId, out option);
        }

        // Used by the cascade: it is expressed in symbols, but it is options that get disabled.
        public static bool TryGetDefineOption(string symbol, out DefineOption option)
        {
            EnsureIndex();

            if (string.IsNullOrEmpty(symbol))
            {
                option = null;
                return false;
            }

            return _bySymbol.TryGetValue(symbol, out option);
        }

        public static bool TryGetSection(string sectionId, out ProjectOptionSection section)
        {
            section = Sections.FirstOrDefault(s => s.Id == sectionId);
            return section != null;
        }

        public static IEnumerable<ProjectOption> AllOptions()
        {
            foreach (ProjectOptionSection section in Sections)
            {
                foreach (ProjectOption option in section.Options)
                    yield return option;
            }
        }

        private static void EnsureIndex()
        {
            if (_byId != null)
                return;

            _byId = new Dictionary<string, ProjectOption>();
            _bySymbol = new Dictionary<string, DefineOption>();

            foreach (ProjectOption option in AllOptions())
            {
                _byId[option.Id] = option;

                if (option is DefineOption define)
                    _bySymbol[define.Symbol] = define;
            }
        }

        private static IReadOnlyList<ProjectOptionSection> Build()
        {
            List<(ProjectOptionSection Section, string Provider)> found =
                ProviderDiscovery.Collect<IProjectOptionSectionProvider, ProjectOptionSection>(
                    provider => provider.GetSections());

            // The provider type name is part of the sort key, so the order stays
            // stable across reloads when two sections share Order and title.
            found.Sort((left, right) =>
            {
                int byOrder = left.Section.Order.CompareTo(right.Section.Order);
                if (byOrder != 0)
                    return byOrder;

                int byTitle = string.Compare(
                    left.Section.Title, right.Section.Title, StringComparison.Ordinal);
                if (byTitle != 0)
                    return byTitle;

                return string.Compare(left.Provider, right.Provider, StringComparison.Ordinal);
            });

            var sectionIds = new Dictionary<string, string>();
            var optionIds = new Dictionary<string, string>();
            var result = new List<ProjectOptionSection>();

            foreach ((ProjectOptionSection section, string provider) in found)
            {
                if (sectionIds.TryGetValue(section.Id, out string owner))
                {
                    Debug.LogError(
                        $"Build options: section id '{section.Id}' is declared by both {owner} " +
                        $"and {provider}. Keeping the first and ignoring the second.");
                    continue;
                }

                sectionIds[section.Id] = provider;

                var options = new List<ProjectOption>();

                foreach (ProjectOption option in section.Options)
                {
                    if (option == null)
                        continue;

                    if (optionIds.TryGetValue(option.Id, out string optionOwner))
                    {
                        Debug.LogError(
                            $"Build options: option id '{option.Id}' is declared by both " +
                            $"{optionOwner} and {provider}. Keeping the first.");
                        continue;
                    }

                    optionIds[option.Id] = provider;
                    options.Add(option);
                }

                options.Sort((left, right) =>
                {
                    int byOrder = left.Order.CompareTo(right.Order);
                    return byOrder != 0
                        ? byOrder
                        : string.Compare(left.Label, right.Label, StringComparison.Ordinal);
                });

                var actions = section.Actions.Where(a => a != null).ToList();
                actions.Sort((left, right) => left.Order.CompareTo(right.Order));

                result.Add(new ProjectOptionSection(
                    section.Id, section.Title, section.Order, options, section.Description, actions));
            }

            return result;
        }
    }
}
