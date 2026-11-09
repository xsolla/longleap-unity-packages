using System;
using UnityEditor;
using UnityEngine;

namespace LongLeap.GamePix.Editor
{
    // Puts the GAMEPIX constraint back into the vendor plugin asmdefs whenever
    // they are imported: every plugin update (a new .zip from GamePix extracted
    // into Assets) overwrites them without it, and a regular WebGL build then pulls the
    // plugin's externs and auto-bootstrap in. The manual repair (menu item and
    // Build Manager row) stays for projects where this watcher was not loaded at
    // import time, e.g. the plugin was updated before this package was added.
    //
    // Two triggers, because an asmdef import usually recompiles and reloads the
    // domain, which drops a pending delayCall: the import callback covers the
    // case without a reload, the [InitializeOnLoad] check covers the reload
    // that follows the import. The check only reads two small files unless the
    // constraint is actually missing.
    //
    // The constraint is always wanted, so there is no setting to turn this off.
    [InitializeOnLoad]
    internal sealed class GamePixPluginImportWatcher : AssetPostprocessor
    {
        private static bool _scheduled;

        static GamePixPluginImportWatcher()
        {
            // AssetDatabase work is not safe during domain load; wait one tick.
            Schedule();
        }

        private static void Schedule()
        {
            if (_scheduled)
                return;

            _scheduled = true;
            EditorApplication.delayCall += RestoreAfterImport;
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            // Files must not be rewritten from inside an import callback; wait
            // until the import that brought the plugin in has finished.
            if (TouchesPluginAsmdef(importedAssets))
                Schedule();
        }

        private static bool TouchesPluginAsmdef(string[] importedAssets)
        {
            foreach (string asset in importedAssets)
            {
                foreach (string path in GamePixAsmdefConstraints.AsmdefPaths)
                {
                    if (string.Equals(asset, path, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        private static void RestoreAfterImport()
        {
            _scheduled = false;

            if (!GamePixAsmdefConstraints.IsPluginVisible() || GamePixAsmdefConstraints.AreInPlace())
                return;

            int patched = GamePixAsmdefConstraints.RestoreFiles(Debug.LogError);
            if (patched == 0)
                return;

            AssetDatabase.Refresh();
            Debug.Log(
                $"GamePix: the plugin was (re)imported without the {GamePixAsmdefConstraints.DefineSymbol} " +
                $"constraint; restored it in {patched} asmdef(s). The plugin compiles only with " +
                $"{GamePixAsmdefConstraints.DefineSymbol} again.");
        }
    }
}
