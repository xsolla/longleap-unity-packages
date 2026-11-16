using System;

namespace LongLeap.BuildManager.Options
{
    // An option whose read and write are arbitrary code.
    //
    // Keeps Core free of package-specific details. For example, a vendor
    // plugin's asmdef repair reads two files from disk with a regex and adds
    // a symbol to them; all of that logic stays in its own package and
    // arrives here as two delegates.
    public sealed class CustomOption : ProjectOption
    {
        private readonly Func<IProjectReadContext, string> _read;
        private readonly Action<string, IProjectWriteContext> _write;
        private readonly Func<IProjectOptionState, ProjectOptionAvailability> _availability;

        public ProjectOptionConfirmation Confirmation { get; }

        public string AppliedNote { get; }

        public CustomOption(
            string id,
            string label,
            string tooltip,
            bool required,
            int order,
            Func<IProjectReadContext, string> read,
            Action<string, IProjectWriteContext> write,
            Func<IProjectOptionState, ProjectOptionAvailability> availability = null,
            bool oneWay = false,
            ProjectOptionConfirmation confirmation = null,
            string appliedNote = null,
            bool visibleInWindow = true)
            : base(id, label, tooltip, required, order, oneWay, visibleInWindow)
        {
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _write = write ?? throw new ArgumentNullException(nameof(write));
            _availability = availability;
            Confirmation = confirmation;
            AppliedNote = appliedNote;
        }

        public override string Read(IProjectReadContext context) => _read(context);

        public override void Write(string value, IProjectWriteContext context) => _write(value, context);

        public override ProjectOptionAvailability GetAvailability(IProjectOptionState state) =>
            _availability?.Invoke(state) ?? ProjectOptionAvailability.Available;

        public override ProjectOptionConfirmation GetConfirmation(string newValue) =>
            newValue == On ? Confirmation : null;

        public override string DescribeApplied(string value) =>
            value == On ? AppliedNote : null;
    }
}
