using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using LongLeap.BuildManager.Options;

namespace LongLeap.GamePix.Editor
{
    // Knows where the vendor plugin lives and repairs the GAMEPIX constraint in
    // its asmdefs.
    //
    // The repair is not a define and not a toggle but a one-way fix of files on
    // disk, so the options table describes it as a separate kind of option
    // (CustomOption in GamePixOptions). AssetDatabase.Refresh is not called
    // here; it is requested through the write context so one Apply does one
    // Refresh rather than one per option.
    internal static class GamePixAsmdefConstraints
    {
        public const string DefineSymbol = "GAMEPIX";

        // The paths are fixed: the vendor code is hard-wired to
        // Assets/Plugins/GamePix (Builder.cs, Emscripten.cs), the folder cannot
        // be moved.
        public const string PluginPath = "Assets/Plugins/GamePix";

        // Present only in plugin versions with the user API (Gpx.User), which
        // XsollaIdAuthGamePix needs.
        public const string UserApiPath = PluginPath + "/User/IUser.cs";

        // Both plugin assemblies must be constrained: the runtime one so a
        // regular WebGL build does not pull in its externs and auto-bootstrap,
        // the editor one because Builder.cs uses ICSharpCode from the runtime
        // assembly and does not compile without it.
        private static readonly string[] PluginAsmdefPaths =
        {
            PluginPath + "/GamePix.asmdef",
            PluginPath + "/Editor/GamePix.Editor.asmdef"
        };

        // defineConstraints in an asmdef is an array of plain strings with no
        // nested brackets, so [^\]]* is safe. The line's indent is captured to
        // write in the same style and keep the diff in the vendor file small.
        private static readonly Regex ConstraintsRegex = new Regex(
            "(?<indent>[ \\t]*)\"defineConstraints\"\\s*:\\s*\\[(?<body>[^\\]]*)\\]",
            RegexOptions.Compiled);

        public static bool IsPluginVisible() => Directory.Exists(PluginPath);

        public static bool HasUserApi() => File.Exists(UserApiPath);

        // The constraints count as in place only if both assemblies have them:
        // a half-applied state breaks compilation (see ICSharpCode in Builder.cs).
        public static bool AreInPlace()
        {
            foreach (string path in PluginAsmdefPaths)
            {
                if (!File.Exists(path) || !HasConstraint(path))
                    return false;
            }

            return true;
        }

        // Returns the number of files patched.
        public static int Restore(IProjectWriteContext context)
        {
            int patched = 0;

            foreach (string path in PluginAsmdefPaths)
            {
                if (!File.Exists(path))
                {
                    context.LogError(
                        $"GamePixAsmdefConstraints: {path} not found — the plugin is damaged or was not fully reinstalled.");
                    continue;
                }

                if (TryAddConstraint(path, context))
                    patched++;
            }

            if (patched == 0)
            {
                context.Log("GamePixAsmdefConstraints: the constraints are already in place, nothing changed.");
                return 0;
            }

            // The files are edited bypassing the AssetDatabase, so a refresh is
            // needed — but one per Apply, not one per file.
            context.RequestAssetRefresh();

            context.Log(
                $"GamePixAsmdefConstraints: the {DefineSymbol} constraint was restored in {patched} asmdef(s). " +
                "The plugin is switched off together with the define again; a regular WebGL build no longer pulls it in.");

            return patched;
        }

        private static bool HasConstraint(string asmdefPath)
        {
            Match match = ConstraintsRegex.Match(File.ReadAllText(asmdefPath));

            if (!match.Success)
                return false;

            foreach (string entry in ParseEntries(match.Groups["body"].Value))
            {
                if (entry == DefineSymbol)
                    return true;
            }

            return false;
        }

        // Returns true if the file was actually changed.
        private static bool TryAddConstraint(string asmdefPath, IProjectWriteContext context)
        {
            string text = File.ReadAllText(asmdefPath);

            Match match = ConstraintsRegex.Match(text);

            if (!match.Success)
            {
                context.LogError(
                    $"GamePixAsmdefConstraints: no defineConstraints field in {asmdefPath} — " +
                    "the asmdef format has changed, add the constraint manually.");
                return false;
            }

            List<string> entries = ParseEntries(match.Groups["body"].Value);

            if (entries.Contains(DefineSymbol))
                return false;

            entries.Add(DefineSymbol);

            string indent = match.Groups["indent"].Value;
            string newLine = text.Contains("\r\n") ? "\r\n" : "\n";

            StringBuilder replacement = new StringBuilder();
            replacement.Append(indent).Append("\"defineConstraints\": [").Append(newLine);

            for (int i = 0; i < entries.Count; i++)
            {
                replacement.Append(indent).Append("    \"").Append(entries[i]).Append('"');

                if (i < entries.Count - 1)
                    replacement.Append(',');

                replacement.Append(newLine);
            }

            replacement.Append(indent).Append(']');

            // Replace only this field — the rest of the file is not rewritten,
            // so the diff in the vendor file stays readable.
            text = text.Remove(match.Index, match.Length).Insert(match.Index, replacement.ToString());

            File.WriteAllText(asmdefPath, text);

            return true;
        }

        private static List<string> ParseEntries(string arrayBody)
        {
            List<string> entries = new List<string>();

            foreach (string raw in arrayBody.Split(','))
            {
                string entry = raw.Trim().Trim('"').Trim();

                if (!string.IsNullOrEmpty(entry))
                    entries.Add(entry);
            }

            return entries;
        }
    }
}
