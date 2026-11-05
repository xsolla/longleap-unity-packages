using System.Collections.Generic;
using LongLeap.BuildManager.Options;

namespace LongLeap.GamePix.Editor
{
    internal sealed class GamePixWarnings : IProjectWarningProvider
    {
        public IEnumerable<ProjectWarning> GetWarnings(IProjectOptionState pendingState)
        {
            if (pendingState == null || !pendingState.IsDefineOn(GamePixOptions.DefineSymbol))
                yield break;

            // The define is on but the plugin is missing: building is pointless,
            // code under #if GAMEPIX will not compile at all.
            if (!GamePixAsmdefConstraints.IsPluginVisible())
            {
                yield return new ProjectWarning(
                    ProjectWarningSeverity.Error,
                    $"GAMEPIX is on but {GamePixAsmdefConstraints.PluginPath} does not exist. " +
                    "Install the vendor plugin or turn GAMEPIX off.",
                    blocksBuild: true);

                yield break;
            }

            // Half-applied constraints break compilation: Builder.cs takes
            // ICSharpCode from the plugin's runtime assembly.
            if (!GamePixAsmdefConstraints.AreInPlace())
            {
                yield return new ProjectWarning(
                    ProjectWarningSeverity.Warning,
                    "The GamePix plugin asmdefs have lost their GAMEPIX defineConstraints — a " +
                    "plugin update wipes them. A plain WebGL build would drag the plugin in. " +
                    "Tick \"Plugin Has defineConstraints\" and Apply.");
            }

            bool jsBridge = pendingState.IsDefineOn(GamePixOptions.JsBridgeDefineSymbol);
            bool userApi = GamePixAsmdefConstraints.HasUserApi();

            // Without GAMEPIX_JS the package compiles XsollaIdAuthGamePix, which
            // needs GamePix.User from the plugin.
            if (!jsBridge && !userApi)
            {
                yield return new ProjectWarning(
                    ProjectWarningSeverity.Error,
                    $"The GamePix plugin has no user API ({GamePixAsmdefConstraints.UserApiPath} is " +
                    "missing), so XsollaIdAuthGamePix cannot compile. Turn on " +
                    "\"Xsolla ID via JS Bridge (GAMEPIX_JS)\" or update the plugin.",
                    blocksBuild: true);
            }
            else if (jsBridge && userApi)
            {
                yield return new ProjectWarning(
                    ProjectWarningSeverity.Info,
                    "GAMEPIX_JS is on although the plugin has Gpx.User. XsollaIdAuthGamePix " +
                    "(GAMEPIX_JS off) is the more reliable sign-in path in WebGL.");
            }
        }
    }
}
