#if UNITY_EDITOR
using System.Collections.Generic;
using LongLeap.Core.Editor;
using UnityEditor;

namespace LongLeap.XsollaId.Editor
{
    // Замечания о конфиге входа.
    //
    // Среда в окне Build Manager не показывается (это выбор из двух, у него
    // своё меню), но её НЕПРАВИЛЬНОСТЬ показывается — иначе окно с блоком
    // BackPack и без блока Xsolla ID наводило бы на мысль, что со входом всё
    // в порядке. Сейчас как раз не в порядке: активна Production, а redirect
    // uri у неё смотрит на 127.0.0.1.
    internal sealed class XsollaIdWarnings : IProjectWarningProvider
    {
        private const string LocalHostPrefix = "http://127.0.0.1";

        public IEnumerable<ProjectWarning> GetWarnings(IProjectOptionState pendingState)
        {
            // Без включённого входа конфиг ни на что не влияет.
            if (pendingState == null || !pendingState.IsDefineOn("XSOLLA_ID"))
                yield break;

            SerializedProperty environment =
                XsollaIdConfigAccess.LoadEnvironmentProperty(out SerializedObject config);

            if (environment == null)
            {
                yield return new ProjectWarning(
                    ProjectWarningSeverity.Error,
                    $"XSOLLA_ID is on but {XsollaIdConfigAccess.ConfigAssetPath} was not found. " +
                    "Sign-in will report Unavailable.",
                    blocksBuild: true);

                yield break;
            }

            string current = XsollaIdConfigAccess.CurrentEnvironment();

            if (current == null)
                yield break;

            string prefix = XsollaIdConfigAccess.FieldPrefix(current);

            string clientId = XsollaIdConfigAccess.GetString(config, prefix + "ClientId");
            string redirectUri = XsollaIdConfigAccess.GetString(config, prefix + "RedirectUri");

            // То же, о чём предупреждает WarnIfIncomplete при переключении, но
            // видно без переключения.
            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(redirectUri))
            {
                yield return new ProjectWarning(
                    ProjectWarningSeverity.Error,
                    $"Xsolla ID environment {current}: " +
                    (string.IsNullOrEmpty(clientId) ? $"{prefix}ClientId " : $"{prefix}RedirectUri ") +
                    "is empty, so sign-in will report Unavailable and the button will stay disabled.",
                    blocksBuild: true,
                    fixLabel: "Select Config Asset",
                    fix: XsollaIdConfigAccess.SelectConfigAsset);

                yield break;
            }

            // Прод, смотрящий на localhost, собирается и запускается — и
            // молча не пускает никого, кроме того, кто собрал.
            if (current == XsollaIdConfigAccess.ProductionEnumName
                && redirectUri.StartsWith(LocalHostPrefix))
            {
                yield return new ProjectWarning(
                    ProjectWarningSeverity.Warning,
                    $"Xsolla ID is set to Production, but {prefix}RedirectUri still points at " +
                    $"{redirectUri}. A hosted build will fail sign-in: that redirect uri only " +
                    "resolves on the machine that built it.",
                    fixLabel: "Select Config Asset",
                    fix: XsollaIdConfigAccess.SelectConfigAsset);
            }
        }
    }
}
#endif
