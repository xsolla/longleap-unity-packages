using LongLeap.Core.Editor;
using UnityEditor;

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
    // Сами настройки описаны в GamePixOptions, а работа с файлами asmdef — в
    // GamePixAsmdefConstraints. Здесь остались только пункты меню, и остаться
    // они обязаны: путь у MenuItem — константа времени компиляции, и метод
    // нужен на каждый пункт, поэтому генерировать пункты из таблицы нельзя.
    // Зато из той же таблицы рисуется окно Build Manager, и разойтись с меню
    // оно не может.
    //
    // Подсказок к пунктам меню нет и быть не может: у атрибута MenuItem только
    // menuItem/priority/secondaryPriority/validate/editorModes, тултипы Unity
    // для меню не поддерживает. Раньше подробности уезжали в Debug.Log и
    // читались уже после переключения — теперь они лежат в Tooltip рядом с
    // настройкой, и окно показывает их до нажатия. В журнал они по-прежнему
    // попадают тоже.
    //
    // Почему сборка НЕ ограничена defineConstraints и не ссылается на
    // LongLeap.GamePix: скрипт, который включает GAMEPIX, не может сам требовать
    // GAMEPIX — иначе он исчезнет ровно тогда, когда нужен. По той же причине он
    // живёт в пакете, а не в Assets/Plugins/GamePix: обновление плагина требует
    // удалить папку целиком, и меню удалилось бы вместе с ней. Ссылка на
    // LongLeap.Core.Editor правило не нарушает: ссылка — не ограничение, и сама
    // LongLeap.Core.Editor тоже ничем не ограничена.
    public static class GamePixPluginMenu
    {
        // Меню разбито на два подменю: в Required лежит то, без чего
        // портальная сборка либо не соберётся, либо не пройдёт ревью;
        // в Optional — то, что нужно только на время отладки и в готовую
        // сборку попадать не должно. Package HTML5 Zip остаётся в корне:
        // это действие, а не переключатель состояния проекта.
        private const string EnabledMenuPath = "GamePix/Required/Plugin Enabled (GAMEPIX)";
        private const string ConstraintsMenuPath = "GamePix/Required/Plugin Has defineConstraints";
        private const string AnalyticsMenuPath = "GamePix/Required/Allow External Analytics (GA4)";
        private const string AdDebugMenuPath = "GamePix/Optional/Show Ad Debug Overlay";
        private const string InjectSdkMenuPath = "GamePix/Required/Inject SDK Tag Into index.html";

        // ---------- Галочка 1: дефайн ----------

        [MenuItem(EnabledMenuPath, false, 200)]
        private static void TogglePluginEnabled() => ProjectOptionMenu.Toggle(GamePixOptions.EnabledId);

        [MenuItem(EnabledMenuPath, true)]
        private static bool ValidateTogglePluginEnabled() =>
            ProjectOptionMenu.Validate(EnabledMenuPath, GamePixOptions.EnabledId);

        // ---------- Галочка 2: ограничения в asmdef'ах плагина ----------

        [MenuItem(ConstraintsMenuPath, false, 202)]
        private static void RestoreConstraints() =>
            ProjectOptionMenu.Toggle(GamePixOptions.ConstraintsId);

        [MenuItem(ConstraintsMenuPath, true)]
        private static bool ValidateRestoreConstraints() =>
            ProjectOptionMenu.Validate(ConstraintsMenuPath, GamePixOptions.ConstraintsId);

        // ---------- Галочка 3: внешняя аналитика ----------

        [MenuItem(AnalyticsMenuPath, false, 203)]
        private static void ToggleAllowAnalytics() =>
            ProjectOptionMenu.Toggle(GamePixOptions.AnalyticsId);

        [MenuItem(AnalyticsMenuPath, true)]
        private static bool ValidateToggleAllowAnalytics() =>
            ProjectOptionMenu.Validate(AnalyticsMenuPath, GamePixOptions.AnalyticsId);

        // ---------- Галочка 4: экранный журнал рекламы ----------

        [MenuItem(AdDebugMenuPath, false, 204)]
        private static void ToggleAdDebug() => ProjectOptionMenu.Toggle(GamePixOptions.AdDebugId);

        [MenuItem(AdDebugMenuPath, true)]
        private static bool ValidateToggleAdDebug() =>
            ProjectOptionMenu.Validate(AdDebugMenuPath, GamePixOptions.AdDebugId);

        // ---------- Галочка 5: тег SDK в index.html ----------

        [MenuItem(InjectSdkMenuPath, false, 205)]
        private static void ToggleInjectSdk() => ProjectOptionMenu.Toggle(GamePixOptions.InjectSdkId);

        [MenuItem(InjectSdkMenuPath, true)]
        private static bool ValidateToggleInjectSdk() =>
            ProjectOptionMenu.Validate(InjectSdkMenuPath, GamePixOptions.InjectSdkId);
    }
}
