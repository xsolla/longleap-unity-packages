#if UNITY_EDITOR
using System.Collections.Generic;
using LongLeap.Core.Editor;
using UnityEditor;

namespace LongLeap.XsollaId.Editor
{
    // Build Manager warnings about the sign-in config.
    //
    // The environment itself is not shown in the Build Manager window (it is a
    // choice between two values with its own menu), but problems with it are:
    // otherwise a window without an Xsolla ID block would suggest sign-in is
    // fine — for example when Production is active but its redirect uri still
    // points at 127.0.0.1.
    internal sealed class XsollaIdWarnings : IProjectWarningProvider
    {
        private const string LocalHostPrefix = "http://127.0.0.1";

        public IEnumerable<ProjectWarning> GetWarnings(IProjectOptionState pendingState)
        {
            // With sign-in off the config affects nothing.
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

            // Same check as WarnIfIncomplete on switching, but visible without
            // switching.
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

            // Production pointing at localhost builds and runs — and silently
            // lets nobody in except the person who built it.
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
