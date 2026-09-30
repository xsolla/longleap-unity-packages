using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace LongLeap.Core.Editor
{
    // Единственное место, где проект читает и пишет символы компиляции.
    //
    // До этой таблицы то же самое было написано ЧЕТЫРЕ раза и по-разному:
    // GamePixPluginMenu через перегрузку с out string[], XsollaIdMenu и
    // DevToolsMenu через строковую перегрузку со Split(';'), а
    // XsollaIdCallbackPagePostprocessor разбирал строку сам ещё раз. Все
    // четыре работали, но одна реализация лучше четырёх.
    public static class ScriptingDefines
    {
        // Дефайны хранятся отдельно на каждую платформу. Проект задаёт их
        // только для WebGL — это записано в комментарии каждого из трёх меню и
        // подтверждается ProjectSettings.asset, где в scriptingDefineSymbols
        // есть ровно одна строка, WebGL.
        //
        // Если пакет понадобится в проекте с другой целевой платформой,
        // менять надо здесь: платформу придётся протащить в DefineOption и
        // группировать записи по платформе в ProjectWriteContext.Flush.
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

        // Возвращает true, если набор действительно изменился.
        //
        // Пишем ОТСОРТИРОВАННЫМ: прежний код дописывал символ в конец, из-за
        // чего строка 835 в ProjectSettings.asset меняла порядок в
        // зависимости от последовательности нажатий. Файл правят трое, и это
        // был бесплатный источник конфликтов при мерже.
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
