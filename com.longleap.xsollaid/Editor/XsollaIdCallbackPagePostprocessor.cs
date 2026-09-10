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
    // Кладёт xid-callback.html в корень собранной игры.
    //
    // Страница обязана быть доступна РОВНО по зарегистрированному адресу:
    // сервер сверяет redirect_uri посимвольно. Отсюда корень сборки —
    // http://127.0.0.1:8081/xid-callback.html локально (serve.py отдаёт
    // Builds/WebGL) и <домен>/xid-callback.html в проде.
    //
    // Почему постпроцесс, а не файл в Assets: в Assets он попал бы в сборку
    // как ассет (и не оказался бы отдельным файлом рядом с index.html), а
    // менять шаблон WebGL нельзя — плагин GamePix перезаписывает настройки
    // шаблона на каждый билд. Тот же приём и по той же причине использует
    // GamePixIndexPostprocessor.
    //
    // Дефайном XSOLLA_ID не огорожено намеренно: класс должен компилироваться
    // всегда, чтобы предупредить, если страницу забыли, и убрать её, когда
    // вход выключен.
    public sealed class XsollaIdCallbackPagePostprocessor : IPostprocessBuildWithReport
    {
        private const string FileName = "xid-callback.html";

        // После GamePix (0), чтобы не мешать его правкам index.html.
        public int callbackOrder => 100;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.WebGL)
                return;

            string destination = Path.Combine(report.summary.outputPath, FileName);

            if (!IsXsollaIdEnabled())
            {
                // Вход выключен — страница в сборке только сбивает с толку.
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

        // Ищем файл рядом с исходником этого класса: путь пакета может быть
        // и Packages/, и Library/PackageCache/, так что зашивать его нельзя.
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
