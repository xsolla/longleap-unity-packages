using System;
using System.Collections.Generic;

namespace LongLeap.Core.Editor
{
    // A toggle backed by a single scripting define symbol.
    public sealed class DefineOption : ProjectOption
    {
        private readonly Func<IProjectOptionState, ProjectOptionAvailability> _availability;

        public string Symbol { get; }

        // Symbols removed TOGETHER with this one when it is turned off.
        //
        // This is NOT the same as GetAvailability; both behaviours exist and
        // they differ:
        //
        //   a sign-in define turned off -> its debug define is REMOVED from
        //                                  the list (nothing to debug without
        //                                  sign-in);
        //   a portal define turned off  -> its companion defines STAY in the
        //                                  list; their options are only
        //                                  disabled.
        //
        // So there is no general "unavailable means off" rule: it would
        // silently change the second behaviour. Only options that need the
        // cascade declare it.
        public IReadOnlyList<string> CascadeOffSymbols { get; }

        public ProjectOptionConfirmation Confirmation { get; }

        public string EnabledNote { get; }

        public string DisabledNote { get; }

        public DefineOption(
            string id,
            string symbol,
            string label,
            string tooltip,
            bool required,
            int order,
            Func<IProjectOptionState, ProjectOptionAvailability> availability = null,
            IReadOnlyList<string> cascadeOffSymbols = null,
            ProjectOptionConfirmation confirmation = null,
            string enabledNote = null,
            string disabledNote = null,
            bool visibleInWindow = true)
            : base(id, label, tooltip, required, order, oneWay: false, visibleInWindow: visibleInWindow)
        {
            if (string.IsNullOrEmpty(symbol))
                throw new ArgumentException($"Option '{id}' must name a define symbol.", nameof(symbol));

            Symbol = symbol;
            _availability = availability;
            CascadeOffSymbols = cascadeOffSymbols ?? Array.Empty<string>();
            Confirmation = confirmation;
            EnabledNote = enabledNote;
            DisabledNote = disabledNote;
        }

        public override string Read(IProjectReadContext context) =>
            context != null && context.Defines.Contains(Symbol) ? On : Off;

        public override void Write(string value, IProjectWriteContext context)
        {
            if (context == null)
                return;

            bool on = value == On;

            context.SetDefine(Symbol, on);

            if (on)
                return;

            foreach (string dependent in CascadeOffSymbols)
                context.SetDefine(dependent, false);
        }

        public override ProjectOptionAvailability GetAvailability(IProjectOptionState state) =>
            _availability?.Invoke(state) ?? ProjectOptionAvailability.Available;

        // Ask only when turning ON: turning off a deviation from a platform's
        // terms needs no confirmation.
        public override ProjectOptionConfirmation GetConfirmation(string newValue) =>
            newValue == On ? Confirmation : null;

        public override string DescribeApplied(string value) =>
            value == On ? EnabledNote : DisabledNote;
    }
}
