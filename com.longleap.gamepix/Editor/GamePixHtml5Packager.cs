using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace LongLeap.GamePix.Editor
{
    // Собирает zip для загрузки игры на площадку как HTML5 (не .gpx).
    //
    // Зачем отдельная кнопка, а не «сжать папку проводником»: у архива есть
    // несколько неочевидных требований, на каждом из которых мы уже спотыкались.
    //
    //   1. Разделитель путей в zip — прямой слэш. Проводник Windows и
    //      Compress-Archive пишут обратный, и по спецификации это невалидно:
    //      распаковщик на Linux (а именно он стоит на приёмке) считает
    //      "Build\html5.wasm" ОДНИМ именем файла, папки Build не появляется, и
    //      игра не грузится. Поэтому имена записей формируем сами.
    //   2. index.html должен лежать в корне архива, а не внутри вложенной папки.
    //   3. Артефакты конвейера .gpx в HTML5-загрузке лишние: MethodMap.tsv на
    //      21 МБ, appinfo.json, libunity-gpx.a и сам .gpx.
    //   4. Тег внешнего SDK со страницы надо снять. На HTML5-маршруте площадка
    //      отдаёт нашу страницу, и лишний скрипт определит window.GamePix раньше,
    //      чем она подставит свой — реклама сломается.
    //
    // Ассеты не пережимает и ничего не пересобирает: только упаковывает то, что
    // уже собрано, и честно рассказывает, что получилось.
    public static class GamePixHtml5Packager
    {
        private const string MenuPath = "GamePix/Package HTML5 Zip...";

        private static readonly HashSet<string> SkipDirs =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Logs" };

        private static readonly HashSet<string> SkipFiles =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "MethodMap.tsv", "appinfo.json", "libunity-gpx.a", ".DS_Store", "Thumbs.db"
            };

        // Скрипт SDK, подставленный в страницу нами или вендором.
        private static readonly Regex ExternalSdkTag = new Regex(
            "[ \\t]*<script src=\"https?://[^\"]*(gamepix|gpxlib)[^\"]*\"></script>\\r?\\n?",
            RegexOptions.IgnoreCase);

        [MenuItem(MenuPath, false, 300)]
        private static void PackageHtml5()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string startDir = Directory.Exists(Path.Combine(projectRoot, "Builds"))
                ? Path.Combine(projectRoot, "Builds")
                : projectRoot;

            string source = EditorUtility.OpenFolderPanel(
                "Папка собранной WebGL-игры (в ней лежит index.html)", startDir, string.Empty);

            if (string.IsNullOrEmpty(source))
                return;

            string indexPath = Path.Combine(source, "index.html");

            if (!File.Exists(indexPath))
            {
                EditorUtility.DisplayDialog("GamePix",
                    "В выбранной папке нет index.html — это не папка сборки WebGL.\n\n" + source, "OK");
                return;
            }

            string html = File.ReadAllText(indexPath);
            bool stripSdk = false;

            if (ExternalSdkTag.IsMatch(html))
            {
                // Спрашиваем, а не решаем сами: для тест-тулкита тег как раз нужен,
                // а для загрузки на площадку он всё ломает.
                stripSdk = EditorUtility.DisplayDialog("GamePix",
                    "Страница подключает внешний скрипт SDK.\n\n" +
                    "Для загрузки на площадку его надо убрать: она подставляет свой SDK, " +
                    "а наш определит window.GamePix раньше и реклама не заработает.\n\n" +
                    "Для прогона в тест-тулките тег, наоборот, нужен.",
                    "Убрать (загрузка на площадку)", "Оставить (тест-тулкит)");
            }

            if (stripSdk)
                html = ExternalSdkTag.Replace(html, string.Empty);

            string defaultName = SanitizeFileName(Application.productName) + "-html5.zip";
            string target = EditorUtility.SaveFilePanel(
                "Куда сохранить архив", projectRoot, defaultName, "zip");

            if (string.IsNullOrEmpty(target))
                return;

            try
            {
                Write(source, target, html);
            }
            catch (OperationCanceledException)
            {
                // Недописанный архив не оставляем: он выглядит как готовый.
                DeleteQuietly(target);
                Debug.Log("GamePixHtml5Packager: упаковка отменена, архив удалён.");
                return;
            }
            catch (Exception e)
            {
                DeleteQuietly(target);
                Debug.LogError("GamePixHtml5Packager: упаковка не удалась — " + e.Message);
                EditorUtility.DisplayDialog("GamePix", "Упаковка не удалась:\n\n" + e.Message, "OK");
                return;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Report(source, target, stripSdk);
        }

        private static void Write(string source, string target, string html)
        {
            List<string> files = Collect(source, target);

            // Пишем через ZipArchive.CreateEntry, а не ZipFile.CreateFromDirectory:
            // последний на Windows подставляет в имена обратный слэш (см. заголовок).
            using (FileStream stream = new FileStream(target, FileMode.Create, FileAccess.Write))
            using (ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                ZipArchiveEntry index = zip.CreateEntry("index.html", System.IO.Compression.CompressionLevel.Optimal);

                using (StreamWriter writer = new StreamWriter(index.Open()))
                    writer.Write(html);

                for (int i = 0; i < files.Count; i++)
                {
                    string full = files[i];
                    string entryName = ToEntryName(source, full);

                    if (EditorUtility.DisplayCancelableProgressBar(
                            "Упаковка HTML5", entryName, (float)i / files.Count))
                        throw new OperationCanceledException("отменено пользователем");

                    ZipArchiveEntry entry = zip.CreateEntry(entryName, System.IO.Compression.CompressionLevel.Optimal);

                    using (Stream to = entry.Open())
                    using (FileStream from = File.OpenRead(full))
                        from.CopyTo(to);
                }
            }
        }

        private static List<string> Collect(string source, string target)
        {
            var result = new List<string>();
            string targetFull = Path.GetFullPath(target);

            foreach (string full in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(full);

                // index.html кладём отдельно: он мог поменяться в памяти.
                if (name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (SkipFiles.Contains(name))
                    continue;

                if (Path.GetExtension(full).Equals(".gpx", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Архив может сохраняться внутрь той же папки — сам себя не пакуем.
                if (Path.GetFullPath(full).Equals(targetFull, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (InSkippedDir(source, full))
                    continue;

                result.Add(full);
            }

            return result;
        }

        private static bool InSkippedDir(string source, string full)
        {
            string relative = ToEntryName(source, full);
            string[] parts = relative.Split('/');

            // Последний элемент — имя файла, каталоги проверяем до него.
            for (int i = 0; i < parts.Length - 1; i++)
                if (SkipDirs.Contains(parts[i]))
                    return true;

            return false;
        }

        private static string ToEntryName(string source, string full)
        {
            string relative = full.Substring(source.Length).TrimStart('/', '\\');

            return relative.Replace('\\', '/');
        }

        // Пересчитываем готовый архив, а не то, что собирались положить: смотрим
        // ровно на результат, включая размер после распаковки — именно его будет
        // качать игрок, и именно он вырастает вчетверо, если сборка без сжатия.
        private static void Report(string source, string target, bool strippedSdk)
        {
            long packed = new FileInfo(target).Length;
            long unpacked = 0;
            int entries = 0;
            bool hasIndexAtRoot = false;
            bool hasBackslash = false;
            bool assetsCompressed = false;

            // Открываем через ZipArchive, а не ZipFile.OpenRead: класс ZipFile
            // живёт в отдельной сборке, которую Unity подключает не всегда.
            using (FileStream stream = File.OpenRead(target))
            using (ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry e in zip.Entries)
                {
                    entries++;
                    unpacked += e.Length;

                    if (e.FullName == "index.html")
                        hasIndexAtRoot = true;

                    if (e.FullName.IndexOf('\\') >= 0)
                        hasBackslash = true;

                    if (e.FullName.EndsWith(".unityweb", StringComparison.OrdinalIgnoreCase)
                        || e.FullName.EndsWith(".br", StringComparison.OrdinalIgnoreCase)
                        || e.FullName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                        assetsCompressed = true;
                }
            }

            Debug.Log(string.Format(
                "GamePixHtml5Packager: собран {0}\n" +
                "  источник: {1}\n" +
                "  записей: {2}, архив: {3:F1} МБ, после распаковки: {4:F1} МБ\n" +
                "  index.html в корне: {5}, обратных слэшей в путях: {6}\n" +
                "  тег внешнего SDK: {7}",
                target, source, entries, Mb(packed), Mb(unpacked),
                hasIndexAtRoot ? "да" : "НЕТ",
                hasBackslash ? "ЕСТЬ" : "нет",
                strippedSdk ? "убран" : "оставлен как был"));

            if (!hasIndexAtRoot)
                Debug.LogError("GamePixHtml5Packager: в корне архива нет index.html — площадка такую сборку не запустит.");

            if (hasBackslash)
                Debug.LogError("GamePixHtml5Packager: в путях архива обратные слэши — на приёмке папки развалятся в плоские имена.");

            if (!assetsCompressed)
                Debug.LogWarning(string.Format(
                    "GamePixHtml5Packager: сборка без сжатия — игрок скачает {0:F0} МБ.\n" +
                    "Сборка из меню GamePix всегда ставит WebGL Compression Format = Disabled " +
                    "(это нужно .gpx, он сжимает сам). Для HTML5-загрузки включите Brotli и " +
                    "Decompression Fallback в Player Settings и соберите через LongLeap / Build Manager.",
                    Mb(unpacked)));

            EditorUtility.RevealInFinder(target);
        }

        private static void DeleteQuietly(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            {
                // Файл занят — не беда, следующая упаковка его перезапишет.
            }
        }

        private static double Mb(long bytes)
        {
            return bytes / 1024.0 / 1024.0;
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '-');

            return name.Replace(' ', '-').ToLowerInvariant();
        }
    }
}
