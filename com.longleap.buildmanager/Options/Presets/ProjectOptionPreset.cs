using System;
using System.Collections.Generic;

namespace LongLeap.BuildManager.Options
{
    // A named set of values, e.g. "build for a web portal" or "standalone
    // HTML5 with Xsolla ID sign-in".
    //
    // A preset addresses options by string Id, not by type, so it can name
    // options from different packages without references between them. That
    // is why a preset can say "portal define ON and sign-in define OFF",
    // which no single package can say about itself.
    //
    // Unknown Ids are skipped with a log message, not an error: in a project
    // without some package, the preset must still apply the rest.
    public sealed class ProjectOptionPreset
    {
        public string Id { get; }

        public string Label { get; }

        public string Tooltip { get; }

        public int Order { get; }

        public IReadOnlyDictionary<string, string> Values { get; }

        // Options the preset does not name are set to their "off" value.
        //
        // This is what lets the "Local" preset mean "sign-in and no other
        // defines" without listing symbols this project may not even have, and
        // what makes switching GamePix -> Xsolla ID really turn GAMEPIX off
        // instead of leaving it on.
        //
        // Options without a natural "off" (environment choices) and one-way
        // options (asmdef repairs) are left alone: there is nothing to reset
        // them to.
        public bool Exhaustive { get; }

        public ProjectOptionPreset(
            string id,
            string label,
            int order,
            IReadOnlyDictionary<string, string> values,
            string tooltip = null,
            bool exhaustive = true)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("Preset id must not be empty.", nameof(id));

            if (string.IsNullOrEmpty(label))
                throw new ArgumentException($"Preset '{id}' must have a label.", nameof(label));

            Id = id;
            Label = label;
            Order = order;
            Values = values ?? new Dictionary<string, string>();
            Tooltip = tooltip;
            Exhaustive = exhaustive;
        }

        public override string ToString() => $"{Id} ({Label})";
    }

    public interface IProjectOptionPresetProvider
    {
        IEnumerable<ProjectOptionPreset> GetPresets();
    }
}
