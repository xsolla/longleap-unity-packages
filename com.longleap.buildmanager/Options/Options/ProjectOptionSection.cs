using System;
using System.Collections.Generic;

namespace LongLeap.BuildManager.Options
{
    // A package's block of options: a title plus a list of options and actions.
    //
    // The Required/Optional split is NOT declared here; it is derived from the
    // options themselves.
    public sealed class ProjectOptionSection
    {
        public string Id { get; }

        public string Title { get; }

        // Optional line under the title.
        public string Description { get; }

        public int Order { get; }

        public IReadOnlyList<ProjectOption> Options { get; }

        // Things that are not project state: "pack a zip", "select the config
        // asset", "restore asmdef constraints". These are actions with no
        // staged value, so there is nothing to defer.
        public IReadOnlyList<ProjectOptionAction> Actions { get; }

        public ProjectOptionSection(
            string id,
            string title,
            int order,
            IReadOnlyList<ProjectOption> options,
            string description = null,
            IReadOnlyList<ProjectOptionAction> actions = null)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("Section id must not be empty.", nameof(id));

            if (string.IsNullOrEmpty(title))
                throw new ArgumentException($"Section '{id}' must have a title.", nameof(title));

            Id = id;
            Title = title;
            Order = order;
            Options = options ?? Array.Empty<ProjectOption>();
            Description = description;
            Actions = actions ?? Array.Empty<ProjectOptionAction>();
        }

        public override string ToString() => $"{Id} ({Title})";
    }

    // A button rather than a toggle.
    public sealed class ProjectOptionAction
    {
        private readonly Func<string> _disabledReason;

        public string Label { get; }

        public string Tooltip { get; }

        public int Order { get; }

        public Action Invoke { get; }

        public ProjectOptionAction(
            string label,
            string tooltip,
            int order,
            Action invoke,
            Func<string> disabledReason = null)
        {
            Label = label;
            Tooltip = tooltip;
            Order = order;
            Invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
            _disabledReason = disabledReason;
        }

        // null when the button is enabled, otherwise the reason it is disabled.
        public string GetDisabledReason() => _disabledReason?.Invoke();
    }

    // A package declares its options by implementing this interface. The
    // registry finds implementations itself; see ProjectOptionRegistry.
    public interface IProjectOptionSectionProvider
    {
        IEnumerable<ProjectOptionSection> GetSections();
    }
}
