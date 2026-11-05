using System;
using System.Collections.Generic;

namespace LongLeap.BuildManager.Options
{
    // Describes one project setting that affects the build: a define toggle,
    // an environment chosen in an asset, a one-off repair of a vendor plugin's
    // asmdefs.
    //
    // Options are data rather than plain menu items because MenuItem has no
    // tooltips: the reason for a toggle lives next to it in Tooltip, and the
    // window shows it before the change is made.
    //
    // Every option value is a string: On/Off for toggles, the enum member name
    // for choices. One representation for all kinds means the staged state
    // serializes as a list of string pairs and survives recompilation, "is it
    // modified" is a single string.Equals, and choices are stored by NAME, not
    // index, so reordering enum members never silently switches a value.
    //
    // An option must not hold UnityEngine.Object references, only paths and
    // delegates. The registry lives until the domain reloads and would
    // otherwise go stale after an AssetDatabase refresh.
    public abstract class ProjectOption
    {
        public const string On = "1";
        public const string Off = "0";

        // Key for presets and staged state. Unlike the label, it must never
        // change, or presets stop finding the option.
        public string Id { get; }

        public string Label { get; }

        public string Tooltip { get; }

        // Required or debug-only. Required options are needed for the build to
        // work or pass a platform review; optional ones must not end up in a
        // release build.
        public bool Required { get; }

        // Matches the MenuItem priority, so rows in the window appear in the
        // same order as the menu items.
        public int Order { get; }

        // The option can only be turned ON. Used for repairs that have no
        // inverse (e.g. fixing asmdefs); the menu item is disabled once there
        // is nothing left to fix.
        public bool OneWay { get; }

        // Whether the option is shown in the window. Hidden options are still
        // available from the menu and still take part in presets.
        public bool VisibleInWindow { get; }

        protected ProjectOption(
            string id,
            string label,
            string tooltip,
            bool required,
            int order,
            bool oneWay = false,
            bool visibleInWindow = true)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("Option id must not be empty.", nameof(id));

            if (string.IsNullOrEmpty(label))
                throw new ArgumentException($"Option '{id}' must have a label.", nameof(label));

            Id = id;
            Label = label;
            Tooltip = tooltip;
            Required = required;
            Order = order;
            OneWay = oneWay;
            VisibleInWindow = visibleInWindow;
        }

        // The "off" value used by exhaustive presets. null means there is no
        // natural "off" (e.g. an environment choice): such an option cannot be
        // reset to nothing, so a preset leaves it alone unless it names it.
        public virtual string OffValue => Off;

        // null for a toggle, otherwise the list of choices.
        public virtual IReadOnlyList<ProjectOptionChoice> Choices => null;

        // Current value. Read only through the context: it reads the define set
        // once and caches one SerializedObject per asset, so querying every
        // option costs the same as querying one.
        public abstract string Read(IProjectReadContext context);

        // Writes the value. The context ACCUMULATES changes and applies them in
        // one go per Apply; see ProjectWriteContext.Flush.
        public abstract void Write(string value, IProjectWriteContext context);

        // Whether the option is available. Evaluated against a VIEW of the
        // state, not the project: the menu passes the saved state, the window
        // passes the staged one, so a row is disabled as soon as the option it
        // depends on is unticked, before Apply.
        public virtual ProjectOptionAvailability GetAvailability(IProjectOptionState state) =>
            ProjectOptionAvailability.Available;

        // Ask for confirmation before writing. For options that are not a
        // convenience but a deviation from a platform's terms.
        public virtual ProjectOptionConfirmation GetConfirmation(string newValue) => null;

        // Message to log after applying, e.g. a reminder to turn a debug option
        // off before submitting the build for review.
        public virtual string DescribeApplied(string value) => null;

        // Human-readable value for the log and for the "(off -> on)" label.
        public virtual string DescribeValue(string value)
        {
            if (Choices == null)
                return value == On ? "on" : "off";

            foreach (ProjectOptionChoice choice in Choices)
            {
                if (choice.Value == value)
                    return choice.Label;
            }

            return string.IsNullOrEmpty(value) ? "<unset>" : value;
        }

        public override string ToString() => $"{Id} ({Label})";
    }

    // One choice: the value is the name of an enum member in the asset.
    public readonly struct ProjectOptionChoice
    {
        public string Value { get; }

        public string Label { get; }

        public string Tooltip { get; }

        public ProjectOptionChoice(string value, string label, string tooltip = null)
        {
            Value = value;
            Label = label;
            Tooltip = tooltip;
        }
    }
}
