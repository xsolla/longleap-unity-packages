using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace LongLeap.GamePix.Editor
{
    // Builds the zip for uploading the game to the portal as HTML5 (not .gpx).
    //
    // Why a dedicated button instead of "compress the folder in Explorer": the
    // archive has several non-obvious requirements, and each one has bitten us.
    //
    //   1. The path separator in a zip is a forward slash. Windows Explorer and
    //      Compress-Archive write backslashes, which is invalid per the spec: a
    //      Linux unpacker (which is what the portal's intake runs) treats
    //      "Build\html5.wasm" as ONE file name, no Build folder appears, and
    //      the game does not load. So the entry names are built here.
    //   2. index.html must be at the archive root, not inside a nested folder.
    //   3. .gpx pipeline artifacts do not belong in an HTML5 upload:
    //      MethodMap.tsv (21 MB), appinfo.json, libunity-gpx.a and the .gpx itself.
    //   4. The GamePix SDK v3 tag as the first script in <head> is mandatory per
    //      the official guide — GamePixIndexPostprocessor adds it, and it stays.
    //      Only the old dev SDK (gpxlib) is removed: it has no ad methods but
    //      would claim window.GamePix.
    //
    // It does not recompress assets or rebuild anything: it only packs what is
    // already built and reports exactly what came out.
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

        // The old dev SDK that GamePixIndexPostprocessor used to insert.
        private static readonly Regex LegacySdkTag = new Regex(
            "[ \\t]*<script src=\"https?://[^\"]*gpxlib[^\"]*\"></script>\\r?\\n?",
            RegexOptions.IgnoreCase);

        [MenuItem(MenuPath, false, 300)]
        private static void PackageHtml5()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string startDir = Directory.Exists(Path.Combine(projectRoot, "Builds"))
                ? Path.Combine(projectRoot, "Builds")
                : projectRoot;

            string source = EditorUtility.OpenFolderPanel(
                "WebGL build folder (contains index.html)", startDir, string.Empty);

            if (string.IsNullOrEmpty(source))
                return;

            string indexPath = Path.Combine(source, "index.html");

            if (!File.Exists(indexPath))
            {
                EditorUtility.DisplayDialog("GamePix",
                    "The selected folder has no index.html — it is not a WebGL build folder.\n\n" + source, "OK");
                return;
            }

            string html = File.ReadAllText(indexPath);
            // Nothing to ask: neither the portal nor the test toolkit needs the old SDK.
            bool stripSdk = LegacySdkTag.IsMatch(html);

            if (stripSdk)
                html = LegacySdkTag.Replace(html, string.Empty);

            if (!html.Contains("integration.gamepix.com/sdk/"))
            {
                Debug.LogWarning("GamePixHtml5Packager: index.html has no GamePix SDK v3 tag — " +
                                 "the guide makes it mandatory. Rebuild the game with GAMEPIX on.");
            }

            string defaultName = SanitizeFileName(Application.productName) + "-html5.zip";
            string target = EditorUtility.SaveFilePanel(
                "Save the archive as", projectRoot, defaultName, "zip");

            if (string.IsNullOrEmpty(target))
                return;

            try
            {
                Write(source, target, html);
            }
            catch (OperationCanceledException)
            {
                // Do not leave a half-written archive: it looks like a finished one.
                DeleteQuietly(target);
                Debug.Log("GamePixHtml5Packager: packaging cancelled, the archive was deleted.");
                return;
            }
            catch (Exception e)
            {
                DeleteQuietly(target);
                Debug.LogError("GamePixHtml5Packager: packaging failed — " + e.Message);
                EditorUtility.DisplayDialog("GamePix", "Packaging failed:\n\n" + e.Message, "OK");
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

            // Written with ZipArchive.CreateEntry, not ZipFile.CreateFromDirectory:
            // the latter puts backslashes into names on Windows (see the header).
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
                            "Packaging HTML5", entryName, (float)i / files.Count))
                        throw new OperationCanceledException("cancelled by the user");

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

                // index.html is written separately: it may have changed in memory.
                if (name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (SkipFiles.Contains(name))
                    continue;

                if (Path.GetExtension(full).Equals(".gpx", StringComparison.OrdinalIgnoreCase))
                    continue;

                // The archive may be saved into the same folder — never pack itself.
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

            // The last part is the file name; check only the folders before it.
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

        // Re-reads the finished archive rather than trusting what was meant to
        // go in: it looks at the actual result, including the unpacked size —
        // that is what the player downloads, and it grows fourfold if the build
        // is uncompressed.
        private static void Report(string source, string target, bool strippedSdk)
        {
            long packed = new FileInfo(target).Length;
            long unpacked = 0;
            int entries = 0;
            bool hasIndexAtRoot = false;
            bool hasBackslash = false;
            bool assetsCompressed = false;

            // Opened through ZipArchive, not ZipFile.OpenRead: ZipFile lives in
            // a separate assembly that Unity does not always reference.
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
                "GamePixHtml5Packager: created {0}\n" +
                "  source: {1}\n" +
                "  entries: {2}, archive: {3:F1} MB, unpacked: {4:F1} MB\n" +
                "  index.html at root: {5}, backslashes in paths: {6}\n" +
                "  legacy SDK tag: {7}",
                target, source, entries, Mb(packed), Mb(unpacked),
                hasIndexAtRoot ? "yes" : "NO",
                hasBackslash ? "YES" : "no",
                strippedSdk ? "removed" : "left as is"));

            if (!hasIndexAtRoot)
                Debug.LogError("GamePixHtml5Packager: no index.html at the archive root — the portal will not start this build.");

            if (hasBackslash)
                Debug.LogError("GamePixHtml5Packager: backslashes in archive paths — the portal's intake will flatten the folders into plain names.");

            if (!assetsCompressed)
                Debug.LogWarning(string.Format(
                    "GamePixHtml5Packager: the build is uncompressed — the player downloads {0:F0} MB.\n" +
                    "A build from the GamePix menu always sets WebGL Compression Format = Disabled " +
                    "(the .gpx needs it, it compresses itself). For an HTML5 upload enable Brotli and " +
                    "Decompression Fallback in Player Settings and build via LongLeap / Build Manager.",
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
                // The file is locked — fine, the next packaging run overwrites it.
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
