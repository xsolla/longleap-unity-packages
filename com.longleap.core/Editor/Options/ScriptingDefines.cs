using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace LongLeap.Core.Editor
{
    // The single place that reads and writes scripting define symbols.
    public static class ScriptingDefines
    {
        // Defines are stored per platform. This class reads and writes them for
        // NamedBuildTarget.WebGL ONLY.
        //
        // To support other platforms, pass the target through DefineOption and
        // group writes per target in ProjectWriteContext.Flush.
        public static readonly NamedBuildTarget Target = NamedBuildTarget.WebGL;

        public static ISet<string> Read()
        {
            PlayerSettings.GetScriptingDefineSymbols(Target, out string[] symbols);

            var defines = new HashSet<string>();

            if (symbols == null)
                return defines;

            foreach (string symbol in symbols)
            {
                string trimmed = symbol?.Trim();

                if (!string.IsNullOrEmpty(trimmed))
                    defines.Add(trimmed);
            }

            return defines;
        }

        public static bool IsOn(string symbol) => Read().Contains(symbol);

        // Returns true if the set actually changed.
        //
        // Symbols are written SORTED, so the define line in
        // ProjectSettings.asset does not change order depending on click
        // order, which would be a needless source of merge conflicts.
        public static bool Write(IEnumerable<string> symbols)
        {
            var target = new HashSet<string>();

            if (symbols != null)
            {
                foreach (string symbol in symbols)
                {
                    string trimmed = symbol?.Trim();

                    if (!string.IsNullOrEmpty(trimmed))
                        target.Add(trimmed);
                }
            }

            if (target.SetEquals(Read()))
                return false;

            PlayerSettings.SetScriptingDefineSymbols(
                Target, target.OrderBy(s => s, System.StringComparer.Ordinal).ToArray());

            return true;
        }
    }
}
