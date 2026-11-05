#if UNITY_EDITOR
using System.Collections.Generic;
using LongLeap.BuildManager.Options;

namespace LongLeap.XsollaId.Editor
{
    internal sealed class XsollaIdOptions : IProjectOptionSectionProvider
    {
        public const string EnabledId = "xsollaid.enabled";
        public const string DebugId = "xsollaid.debug";

        private const string Define = "XSOLLA_ID";
        private const string DebugDefine = "XSOLLA_ID_DEBUG";

        public IEnumerable<ProjectOptionSection> GetSections()
        {
            yield return new ProjectOptionSection(
                "xsollaid",
                "Xsolla ID",
                20,
                new ProjectOption[]
                {
                    new DefineOption(
                        EnabledId,
                        Define,
                        "Sign-in Enabled (XSOLLA_ID)",
                        "Sign-in with Xsolla ID. The LongLeap.XsollaId.Web assembly is gated " +
                        "by this define: without it the assembly does not exist in the project " +
                        "and sign-in is not built. Turning it off also removes the debug panel, " +
                        "since there is nothing to debug without sign-in.",
                        required: true,
                        order: 100,

                        // Removed TOGETHER with sign-in rather than just
                        // becoming unavailable.
                        cascadeOffSymbols: new[] { DebugDefine },
                        enabledNote:
                        "Xsolla ID: sign-in enabled (XSOLLA_ID added). Do not forget to fill in clientId in "
                        + XsollaIdConfigAccess.ConfigAssetPath,
                        disabledNote:
                        "Xsolla ID: sign-in disabled (XSOLLA_ID removed). The build will compile with no network calls to id.xsolla.com."),

                    new DefineOption(
                        DebugId,
                        DebugDefine,
                        "Debug Panel (on-screen)",
                        "Debug sign-in from the keyboard: F9 signs in, F10 signs out, F11 shows " +
                        "the state. Works only in a WebGL build. Must not ship in a portal " +
                        "build.",
                        required: false,
                        order: 101,
                        availability: state => ProjectOptionAvailability.RequireDefine(
                            state, Define, "Sign-in must be on — there is nothing to debug otherwise."),
                        enabledNote:
                        "Xsolla ID: the debug trigger is on — F9 sign in, F10 sign out, F11 state. "
                        + "Works only in a WebGL build; turn it off before submitting to the portal.",
                        disabledNote: "Xsolla ID: the debug sign-in trigger is off."),
                },
                actions: new[]
                {
                    new ProjectOptionAction(
                        "Select Config Asset",
                        $"Select {XsollaIdConfigAccess.ConfigAssetPath} in the project.",
                        102,
                        XsollaIdConfigAccess.SelectConfigAsset),
                });
        }
    }
}
#endif
