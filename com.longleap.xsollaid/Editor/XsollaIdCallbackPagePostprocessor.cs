#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEngine;

namespace LongLeap.XsollaId.Editor
{
    // Copies xid-callback.html into the root of the built game.
    //
    // The page must be reachable at EXACTLY the registered address: the server
    // compares redirect_uri character by character. Hence the build root —
    // e.g. http://127.0.0.1:8081/xid-callback.html on the local dev server and
    // <domain>/xid-callback.html in production.
    //
    // Why a postprocessor rather than a file in Assets: in Assets it would be
    // packed into the build as an asset (not a separate file next to
    // index.html), and the WebGL template cannot be changed because the
    // GamePix plugin overwrites the template settings on every build. The
    // GamePix package's index postprocessor uses the same approach for the
    // same reason.
    //
    // Deliberately not gated by XSOLLA_ID: the class must always compile so it
    // can warn when the page is missing and remove it when sign-in is off.
    public sealed class XsollaIdCallbackPagePostprocessor : IPostprocessBuildWithReport
    {
        private const string FileName = "xid-callback.html";

        // After GamePix (0), so it does not interfere with its index.html edits.
        public int callbackOrder => 100;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.WebGL)
                return;

            string destination = Path.Combine(report.summary.outputPath, FileName);

            if (!IsXsollaIdEnabled())
            {
                // Sign-in is off — the page in the build would only confuse.
                if (File.Exists(destination))
                {
                    File.Delete(destination);
                    Debug.Log($"XsollaId: {FileName} removed from the build — the XSOLLA_ID define is off.");
                }

                return;
            }

            string source = FindSourceFile();

            if (source == null)
            {
                Debug.LogError(
                    $"XsollaId: {FileName} not found in the package — sign-in will not work in the built game. " +
                    "Expected at Editor/Resources/" + FileName);
                return;
            }

            File.Copy(source, destination, overwrite: true);

            Debug.Log($"XsollaId: {FileName} copied into the build ({destination}).");
        }

        private static bool IsXsollaIdEnabled()
        {
            string defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.WebGL);

            return defines != null &&
                   defines.Split(';').Any(d => d.Trim() == "XSOLLA_ID");
        }

        // Look for the file next to this class's source: the package may live in
        // Packages/ or in Library/PackageCache/, so the path cannot be hardcoded.
        private static string FindSourceFile()
        {
            string assemblyName = typeof(XsollaIdCallbackPagePostprocessor).Assembly.GetName().Name;

            Assembly assembly = CompilationPipeline
                .GetAssemblies(AssembliesType.Editor)
                .FirstOrDefault(a => a.name == assemblyName);

            string scriptPath = assembly?.sourceFiles.FirstOrDefault(f =>
                Path.GetFileName(f) == nameof(XsollaIdCallbackPagePostprocessor) + ".cs");

            if (string.IsNullOrEmpty(scriptPath))
                return null;

            string candidate = Path.Combine(Path.GetDirectoryName(scriptPath), "Resources", FileName);

            return File.Exists(candidate) ? candidate : null;
        }
    }
}
#endif
