#if UNITY_EDITOR
using LongLeap.Core.Editor;
using UnityEditor;

namespace LongLeap.DevTools.EditorTools
{
    // Переключатель отладочных инструментов пакета.
    //
    // Сама настройка описана в DevToolsOptions — здесь остался только пункт
    // меню. Иначе нельзя: путь у MenuItem — константа времени компиляции, и
    // метод нужен на каждый пункт, поэтому генерировать пункты из таблицы не
    // получится. Зато тело метода теперь одна строка, а подтверждения, каскад,
    // запись и журнал живут в одном месте на весь проект и совпадают с тем,
    // что делает окно Build Manager.
    //
    // У этой сборки НЕТ defineConstraints намеренно: скрипт, который включает
    // LONGLEAP_DEBUG, не может сам его требовать — иначе включить будет нечем.
    // То же самое сделано в LongLeap.XsollaId.Editor. Ссылка на
    // LongLeap.Core.Editor этого не меняет: ссылка — не ограничение, а сама
    // LongLeap.Core.Editor тоже ничем не ограничена.
    //
    // Символы правятся только для WebGL: это единственная платформа, для
    // которой проект их вообще задаёт. Теперь это записано в одном месте —
    // ScriptingDefines.Target.
    public static class DevToolsMenu
    {
        private const string MenuPath = "LongLeap/Cloud Save Panel (LONGLEAP_DEBUG)";

        [MenuItem(MenuPath, false, 120)]
        private static void Toggle() => ProjectOptionMenu.Toggle(DevToolsOptions.DebugId);

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate() =>
            ProjectOptionMenu.Validate(MenuPath, DevToolsOptions.DebugId);
    }
}
#endif
