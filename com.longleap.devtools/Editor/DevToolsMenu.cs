#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace LongLeap.DevTools.EditorTools
{
    // Переключатель отладочных инструментов пакета.
    //
    // У этой сборки НЕТ defineConstraints намеренно: скрипт, который включает
    // LONGLEAP_DEBUG, не может сам его требовать — иначе включить будет нечем.
    // То же самое сделано в LongLeap.XsollaId.Editor.
    //
    // Символы правятся только для WebGL: это единственная платформа, для которой
    // проект их вообще задаёт.
    public static class DevToolsMenu
    {
        private const string Define = "LONGLEAP_DEBUG";
        private const string MenuPath = "LongLeap/Cloud Save Panel (LONGLEAP_DEBUG)";

        [MenuItem(MenuPath, false, 120)]
        private static void Toggle()
        {
            bool enabled = IsEnabled();
            SetDefine(Define, !enabled);

            Debug.Log(enabled
                ? "DevTools: cloud save debug panel is off."
                : "DevTools: cloud save debug panel is on - turn it off before submitting to the portal.");
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, IsEnabled());
            return true;
        }

        private static bool IsEnabled() => CurrentDefines().Contains(Define);

        private static List<string> CurrentDefines()
        {
            string raw = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.WebGL) ?? string.Empty;
            var defines = new List<string>();

            foreach (string part in raw.Split(';'))
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0)
                    defines.Add(trimmed);
            }

            return defines;
        }

        private static void SetDefine(string define, bool enabled)
        {
            List<string> defines = CurrentDefines();

            if (enabled)
            {
                if (!defines.Contains(define))
                    defines.Add(define);
            }
            else
            {
                defines.Remove(define);
            }

            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.WebGL, string.Join(";", defines));
        }
    }
}
#endif
