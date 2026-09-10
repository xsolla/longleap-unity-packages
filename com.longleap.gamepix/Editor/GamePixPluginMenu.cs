using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace LongLeap.GamePix.Editor
{
    // Галочки меню GamePix. В подменю Required — то, без чего портальная
    // сборка не соберётся или не пройдёт ревью; в Optional — только отладочное.
    //
    //   Required/Plugin Enabled (GAMEPIX)       — дефайн, включающий сборки плагина;
    //   Required/Plugin Has defineConstraints   — стоят ли в asmdef'ах плагина
    //     ограничения по GAMEPIX. Галочка снята — обновление плагина их стёрло,
    //     нажатие вернёт обратно; когда всё на месте, пункт заблокирован;
    //   Required/Allow External Analytics (GA4) — дефайн GAMEPIX_ALLOW_ANALYTICS;
    //   Required/Inject SDK Tag Into index.html — дефайн GAMEPIX_INJECT_SDK;
    //   Optional/Show Ad Debug Overlay          — дефайн GAMEPIX_AD_DEBUG.
    //
    // Подсказок к пунктам нет и быть не может: у атрибута MenuItem только
    // menuItem/priority/secondaryPriority/validate/editorModes, тултипы Unity для
    // меню не поддерживает. Подробности — в Debug.Log при переключении.
    //
    // Почему сборка НЕ ограничена defineConstraints и не ссылается на
    // LongLeap.GamePix: скрипт, который включает GAMEPIX, не может сам требовать
    // GAMEPIX — иначе он исчезнет ровно тогда, когда нужен. По той же причине он
    // живёт в пакете, а не в Assets/Plugins/GamePix: обновление плагина требует
    // удалить папку целиком, и меню удалилось бы вместе с ней.
    public static class GamePixPluginMenu
    {
        private const string DefineSymbol = "GAMEPIX";

        // Разрешение на внешнюю аналитику. По умолчанию выключено: T&C (3.3)
        // запрещают обращаться к любому адресу, кроме площадки, и проверяют это
        // инспекцией трафика. Та же 3.3 разрешает исключение «with our prior
        // written consent» — вот его и включает эта галочка, получив разрешение.
        private const string AnalyticsDefineSymbol = "GAMEPIX_ALLOW_ANALYTICS";

        // Меню разбито на два подменю: в Required лежит то, без чего
        // портальная сборка либо не соберётся, либо не пройдёт ревью;
        // в Optional — то, что нужно только на время отладки и в готовую
        // сборку попадать не должно. Package HTML5 Zip остаётся в корне:
        // это действие, а не переключатель состояния проекта.
        private const string EnabledMenuPath = "GamePix/Required/Plugin Enabled (GAMEPIX)";
        private const string ConstraintsMenuPath = "GamePix/Required/Plugin Has defineConstraints";
        private const string AnalyticsMenuPath = "GamePix/Required/Allow External Analytics (GA4)";

        // Экранный журнал рекламы: в портальной сборке консоль браузера из чужого
        // iframe не достать, а результат показа видеть надо.
        private const string AdDebugDefineSymbol = "GAMEPIX_AD_DEBUG";
        private const string AdDebugMenuPath = "GamePix/Optional/Show Ad Debug Overlay";

        // Вставлять ли тег SDK в index.html после билда. Нужно тест-тулкиту:
        // без window.GamePix плагин молча не стартует. В сборку на ревью не идёт —
        // там SDK подставляет издательский конвейер площадки.
        private const string InjectSdkDefineSymbol = "GAMEPIX_INJECT_SDK";
        private const string InjectSdkMenuPath = "GamePix/Required/Inject SDK Tag Into index.html";

        // Пути жёсткие: вендорный код прибит к Assets/Plugins/GamePix
        // (Builder.cs, Emscripten.cs), переносить папку нельзя.
        private const string PluginPath = "Assets/Plugins/GamePix";

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

        // ---------- Галочка 1: дефайн ----------

        [MenuItem(EnabledMenuPath, false, 200)]
        private static void TogglePluginEnabled()
        {
            SetDefine(!IsDefineOn());
        }

        [MenuItem(EnabledMenuPath, true)]
        private static bool ValidateTogglePluginEnabled()
        {
            Menu.SetChecked(EnabledMenuPath, IsDefineOn());

            // Если папки плагина нет (удалили или переименовали вручную), дефайн
            // включать нельзя: код под #if GAMEPIX будет собираться против
            // отсутствующего Gpx и всё встанет с ошибкой.
            return IsPluginVisible();
        }

        // ---------- Галочка 2: ограничения в asmdef'ах плагина ----------

        [MenuItem(ConstraintsMenuPath, false, 202)]
        private static void RestoreConstraints()
        {
            int patched = 0;

            foreach (string path in PluginAsmdefPaths)
            {
                if (!File.Exists(path))
                {
                    Debug.LogError($"GamePixPluginMenu: не найден {path} — плагин повреждён или переустановлен не полностью.");
                    continue;
                }

                if (TryAddConstraint(path))
                    patched++;
            }

            if (patched == 0)
            {
                Debug.Log("GamePixPluginMenu: ограничения уже на месте, ничего не менялось.");
                return;
            }

            AssetDatabase.Refresh();

            Debug.Log($"GamePixPluginMenu: ограничение {DefineSymbol} восстановлено в {patched} asmdef(ах). " +
                      "Плагин снова выключается вместе с дефайном, обычный WebGL-билд больше его не тянет.");
        }

        [MenuItem(ConstraintsMenuPath, true)]
        private static bool ValidateRestoreConstraints()
        {
            bool constrained = AreConstraintsInPlace();

            Menu.SetChecked(ConstraintsMenuPath, constrained);

            // Пункт активен только когда чинить есть что: галочка снята и папка на месте.
            return IsPluginVisible() && !constrained;
        }

        // ---------- Галочка 3: внешняя аналитика ----------

        [MenuItem(AnalyticsMenuPath, false, 203)]
        private static void ToggleAllowAnalytics()
        {
            bool enabling = !IsDefineOn(AnalyticsDefineSymbol);

            // Включение спрашиваем: это не настройка удобства, а отступление от
            // условий площадки, законное только при письменном разрешении.
            if (enabling && !EditorUtility.DisplayDialog(
                    "GamePix",
                    "Enable external analytics (GA4) in the portal build?\n\n" +
                    "Developer T&C clause 3.3 forbids the game from contacting any network " +
                    "endpoint other than GamePix, and states they verify this by inspecting the " +
                    "game's network requests. Breach allows removal without notice.\n\n" +
                    "The same clause permits an exception \"with our prior written consent\". " +
                    "Only enable this if you actually hold that consent from GamePix.",
                    "I have written consent",
                    "Cancel"))
                return;

            if (!SetDefine(AnalyticsDefineSymbol, enabling))
                return;

            Debug.Log(enabling
                ? "GamePixPluginMenu: внешняя аналитика разрешена — GA4 поднимется и в портальной сборке. " +
                  "Убедитесь, что письменное разрешение площадки действительно есть."
                : "GamePixPluginMenu: внешняя аналитика запрещена — в портальной сборке GA4 выключен (T&C 3.3).");
        }

        [MenuItem(AnalyticsMenuPath, true)]
        private static bool ValidateToggleAllowAnalytics()
        {
            Menu.SetChecked(AnalyticsMenuPath, IsDefineOn(AnalyticsDefineSymbol));

            // Вне портальной сборки дефайн ни на что не влияет: аналитика и так
            // работает, гейт стоит только под GAMEPIX. Не даём щёлкать впустую.
            return IsDefineOn(DefineSymbol);
        }

        // ---------- Галочка 4: экранный журнал рекламы ----------

        [MenuItem(AdDebugMenuPath, false, 204)]
        private static void ToggleAdDebug()
        {
            bool enabling = !IsDefineOn(AdDebugDefineSymbol);

            if (!SetDefine(AdDebugDefineSymbol, enabling))
                return;

            Debug.Log(enabling
                ? "GamePixPluginMenu: экранный журнал рекламы включён — оверлей появится в игре сам, префаб не нужен. " +
                  "Перед отправкой на ревью выключите."
                : "GamePixPluginMenu: экранный журнал рекламы выключен.");
        }

        [MenuItem(AdDebugMenuPath, true)]
        private static bool ValidateToggleAdDebug()
        {
            Menu.SetChecked(AdDebugMenuPath, IsDefineOn(AdDebugDefineSymbol));

            // Оверлей полезен в любой сборке: вне портала он показывает
            // Unavailable, и это тоже ответ.
            return true;
        }

        // ---------- Галочка 5: тег SDK в index.html ----------

        [MenuItem(InjectSdkMenuPath, false, 205)]
        private static void ToggleInjectSdk()
        {
            bool enabling = !IsDefineOn(InjectSdkDefineSymbol);

            if (!SetDefine(InjectSdkDefineSymbol, enabling))
                return;

            Debug.Log(enabling
                ? "GamePixPluginMenu: тег SDK будет вставляться в index.html — это для тест-тулкита. " +
                  "Перед сборкой на ревью выключите: площадка подставляет свой SDK сама."
                : "GamePixPluginMenu: тег SDK в index.html больше не вставляется (режим для .gpx на ревью).");
        }

        [MenuItem(InjectSdkMenuPath, true)]
        private static bool ValidateToggleInjectSdk()
        {
            Menu.SetChecked(InjectSdkMenuPath, IsDefineOn(InjectSdkDefineSymbol));

            // Постпроцесс живёт под #if GAMEPIX — вне портальной сборки его нет.
            return IsDefineOn(DefineSymbol);
        }

        // ---------- Реализация ----------

        private static bool IsPluginVisible() => Directory.Exists(PluginPath);

        private static bool IsDefineOn() => IsDefineOn(DefineSymbol);

        private static bool IsDefineOn(string symbol)
        {
            PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.WebGL, out string[] symbols);
            return Array.IndexOf(symbols, symbol) >= 0;
        }

        private static void SetDefine(bool enabled)
        {
            if (!SetDefine(DefineSymbol, enabled))
                return;

            Debug.Log(enabled
                ? "GamePixPluginMenu: GAMEPIX включён — сборки плагина появятся после перекомпиляции. " +
                  "Не забудьте выключить его перед обычным WebGL-билдом."
                : "GamePixPluginMenu: GAMEPIX выключен — сборки плагина исчезнут после перекомпиляции.");
        }

        // Возвращает true, если набор дефайнов действительно изменился.
        private static bool SetDefine(string symbol, bool enabled)
        {
            PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.WebGL, out string[] current);

            List<string> symbols = new List<string>(current);

            if (enabled == symbols.Contains(symbol))
                return false;

            if (enabled)
                symbols.Add(symbol);
            else
                symbols.Remove(symbol);

            // Дефайны хранятся отдельно на каждую платформу — пишем именно в WebGL,
            // единственную целевую платформу проекта.
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.WebGL, symbols.ToArray());

            return true;
        }

        // Ограничения считаются на месте, только если они есть в обеих сборках:
        // половинчатое состояние ломает компиляцию (см. ICSharpCode в Builder.cs).
        private static bool AreConstraintsInPlace()
        {
            foreach (string path in PluginAsmdefPaths)
            {
                if (!File.Exists(path) || !HasConstraint(path))
                    return false;
            }

            return true;
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
        private static bool TryAddConstraint(string asmdefPath)
        {
            string text = File.ReadAllText(asmdefPath);

            Match match = ConstraintsRegex.Match(text);

            if (!match.Success)
            {
                Debug.LogError($"GamePixPluginMenu: в {asmdefPath} не найдено поле defineConstraints — " +
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
