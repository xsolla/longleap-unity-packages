using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using LongLeap.Core.Editor;

namespace LongLeap.GamePix.Editor
{
    // Ремонт ограничений в asmdef-ах вендорного плагина.
    //
    // Вынесено из GamePixPluginMenu без изменения логики: regexp, сохранение
    // отступа и определение перевода строки — всё как было. Причина выноса в
    // том, что эта операция НЕ дефайн и не переключатель, а односторонний
    // ремонт файлов на диске, и описывать её в общей таблице настроек надо
    // отдельным видом.
    //
    // Единственное изменение поведения: AssetDatabase.Refresh больше не
    // вызывается здесь, а запрашивается через контекст записи — чтобы один
    // Apply делал один Refresh, а не по одному на каждую настройку.
    internal static class GamePixAsmdefConstraints
    {
        public const string DefineSymbol = "GAMEPIX";

        // Пути жёсткие: вендорный код прибит к Assets/Plugins/GamePix
        // (Builder.cs, Emscripten.cs), переносить папку нельзя.
        public const string PluginPath = "Assets/Plugins/GamePix";

        // Обе сборки плагина обязаны быть ограничены: рантайм — чтобы не тянуть
        // externs и авто-бутстрап в обычный WebGL-билд, редакторная — потому что
        // Builder.cs использует ICSharpCode из рантайм-сборки и без неё не собирается.
        private static readonly string[] PluginAsmdefPaths =
        {
            PluginPath + "/GamePix.asmdef",
            PluginPath + "/Editor/GamePix.Editor.asmdef"
        };

        // defineConstraints в asmdef — массив простых строк, вложенных скобок там
        // не бывает, поэтому [^\]]* безопасен. Отступ строки запоминаем, чтобы
        // писать в том же стиле и не плодить лишний диф в вендорном файле.
        private static readonly Regex ConstraintsRegex = new Regex(
            "(?<indent>[ \\t]*)\"defineConstraints\"\\s*:\\s*\\[(?<body>[^\\]]*)\\]",
            RegexOptions.Compiled);

        public static bool IsPluginVisible() => Directory.Exists(PluginPath);

        // Ограничения считаются на месте, только если они есть в обеих сборках:
        // половинчатое состояние ломает компиляцию (см. ICSharpCode в Builder.cs).
        public static bool AreInPlace()
        {
            foreach (string path in PluginAsmdefPaths)
            {
                if (!File.Exists(path) || !HasConstraint(path))
                    return false;
            }

            return true;
        }

        // Возвращает число исправленных файлов.
        public static int Restore(IProjectWriteContext context)
        {
            int patched = 0;

            foreach (string path in PluginAsmdefPaths)
            {
                if (!File.Exists(path))
                {
                    context.LogError(
                        $"GamePixPluginMenu: не найден {path} — плагин повреждён или переустановлен не полностью.");
                    continue;
                }

                if (TryAddConstraint(path, context))
                    patched++;
            }

            if (patched == 0)
            {
                context.Log("GamePixPluginMenu: ограничения уже на месте, ничего не менялось.");
                return 0;
            }

            // Файлы правим в обход AssetDatabase, поэтому обновление нужно —
            // но одно на весь Apply, а не на каждый файл.
            context.RequestAssetRefresh();

            context.Log(
                $"GamePixPluginMenu: ограничение {DefineSymbol} восстановлено в {patched} asmdef(ах). " +
                "Плагин снова выключается вместе с дефайном, обычный WebGL-билд больше его не тянет.");

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

        // Возвращает true, если файл действительно изменён.
        private static bool TryAddConstraint(string asmdefPath, IProjectWriteContext context)
        {
            string text = File.ReadAllText(asmdefPath);

            Match match = ConstraintsRegex.Match(text);

            if (!match.Success)
            {
                context.LogError(
                    $"GamePixPluginMenu: в {asmdefPath} не найдено поле defineConstraints — " +
                    "формат asmdef изменился, добавьте ограничение вручную.");
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

            // Заменяем только это поле — остальной файл не переписываем, чтобы
            // диф в вендорном файле оставался читаемым.
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
