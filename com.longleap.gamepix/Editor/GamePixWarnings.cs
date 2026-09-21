using System.Collections.Generic;
using LongLeap.Core.Editor;

namespace LongLeap.GamePix.Editor
{
    internal sealed class GamePixWarnings : IProjectWarningProvider
    {
        public IEnumerable<ProjectWarning> GetWarnings(IProjectOptionState pendingState)
        {
            if (pendingState == null || !pendingState.IsDefineOn(GamePixOptions.DefineSymbol))
                yield break;

            // Дефайн включён, а плагина нет: собирать бессмысленно, код под
            // #if GAMEPIX не соберётся вовсе.
            if (!GamePixAsmdefConstraints.IsPluginVisible())
            {
                yield return new ProjectWarning(
                    ProjectWarningSeverity.Error,
                    $"GAMEPIX is on but {GamePixAsmdefConstraints.PluginPath} does not exist. " +
                    "Install the vendor plugin or turn GAMEPIX off.",
                    blocksBuild: true);

                yield break;
            }

            // Половинчатые ограничения ломают компиляцию: Builder.cs берёт
            // ICSharpCode из рантайм-сборки плагина.
            if (!GamePixAsmdefConstraints.AreInPlace())
            {
                yield return new ProjectWarning(
                    ProjectWarningSeverity.Warning,
                    "The GamePix plugin asmdefs have lost their GAMEPIX defineConstraints — a " +
                    "plugin update wipes them. A plain WebGL build would drag the plugin in. " +
                    "Tick \"Plugin Has defineConstraints\" and Apply.");
            }
        }
    }
}
