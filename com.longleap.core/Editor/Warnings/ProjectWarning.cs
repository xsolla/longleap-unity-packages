using System;
using System.Collections.Generic;

namespace LongLeap.Core.Editor
{
    public enum ProjectWarningSeverity
    {
        Info,
        Warning,
        Error
    }

    // A note about the configuration, e.g. "production points to 127.0.0.1",
    // "the portal define is on but the plugin is missing", "compression does
    // not match this loading method".
    //
    // Supplied by packages, not hard-coded in the window; otherwise the window
    // would have to know every platform's rules and every config's layout.
    //
    // Evaluated against the STAGED state: a warning shows the consequence of
    // what you are about to apply, not of what is already applied.
    public sealed class ProjectWarning
    {
        public ProjectWarningSeverity Severity { get; }

        public string Message { get; }

        // Warnings never block Apply, only the build: applying settings may be
        // exactly what clears the warning.
        public bool BlocksBuild { get; }

        public string FixLabel { get; }

        public Action Fix { get; }

        public ProjectWarning(
            ProjectWarningSeverity severity,
            string message,
            bool blocksBuild = false,
            string fixLabel = null,
            Action fix = null)
        {
            if (string.IsNullOrEmpty(message))
                throw new ArgumentException("A warning must say something.", nameof(message));

            Severity = severity;
            Message = message;
            BlocksBuild = blocksBuild;
            FixLabel = fixLabel;
            Fix = fix;
        }

        public bool HasFix => Fix != null && !string.IsNullOrEmpty(FixLabel);

        public override string ToString() => $"{Severity}: {Message}";
    }

    public interface IProjectWarningProvider
    {
        IEnumerable<ProjectWarning> GetWarnings(IProjectOptionState pendingState);
    }
}
