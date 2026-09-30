#if UNITY_EDITOR
using System.Collections.Generic;
using LongLeap.Core.Editor;

namespace LongLeap.DevTools.EditorTools
{
    // Таблица настроек пакета. Из неё рисуется и пункт меню, и строка в окне
    // Build Manager, поэтому разойтись они не могут.
    internal sealed class DevToolsOptions : IProjectOptionSectionProvider
    {
        public const string DebugId = "devtools.debug";

        private const string Define = "LONGLEAP_DEBUG";

        public IEnumerable<ProjectOptionSection> GetSections()
        {
            yield return new ProjectOptionSection(
                "devtools",
                "LongLeap Dev Tools",
                40,
                new ProjectOption[]
                {
                    new DefineOption(
                        DebugId,
                        Define,
                        "Cloud Save Panel (LONGLEAP_DEBUG)",
                        "Экранная панель облачных сохранений. Сборка LongLeap.DevTools " +
                        "ограничена этим дефайном, то есть без него панели в проекте нет " +
                        "вовсе. Отладочная — в сборку на площадку попадать не должна.",
                        required: false,
                        order: 120,
                        enabledNote: "DevTools: cloud save debug panel is on - turn it off before submitting to the portal.",
                        disabledNote: "DevTools: cloud save debug panel is off."),
                });
        }
    }
}
#endif
