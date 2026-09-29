using System.Collections.Generic;
using LongLeap.Core.Editor;

namespace LongLeap.GamePix.Editor
{
    internal sealed class GamePixOptions : IProjectOptionSectionProvider
    {
        public const string EnabledId = "gamepix.enabled";
        public const string ConstraintsId = "gamepix.constraints";
        public const string AnalyticsId = "gamepix.allow_analytics";
        public const string AdDebugId = "gamepix.ad_debug";
        public const string InjectSdkId = "gamepix.inject_sdk";

        public const string DefineSymbol = "GAMEPIX";

        private const string AnalyticsDefineSymbol = "GAMEPIX_ALLOW_ANALYTICS";
        private const string AdDebugDefineSymbol = "GAMEPIX_AD_DEBUG";
        private const string InjectSdkDefineSymbol = "GAMEPIX_INJECT_SDK";

        private const string PluginMissing =
            "Assets/Plugins/GamePix is missing — install the vendor plugin first. "
            + "With the folder gone, code under #if GAMEPIX would compile against an absent Gpx.";

        private const string NeedsGamePix =
            "GAMEPIX must be on — outside a portal build this define changes nothing.";

        public IEnumerable<ProjectOptionSection> GetSections()
        {
            yield return new ProjectOptionSection(
                "gamepix",
                "GamePix",
                10,
                new ProjectOption[]
                {
                    new DefineOption(
                        EnabledId,
                        DefineSymbol,
                        "Plugin Enabled (GAMEPIX)",
                        "Дефайн, включающий сборки вендорного плагина площадки. Без папки " +
                        "Assets/Plugins/GamePix включать нельзя: код под #if GAMEPIX будет " +
                        "собираться против отсутствующего Gpx и всё встанет с ошибкой.",
                        required: true,
                        order: 200,
                        availability: PluginPresent,
                        enabledNote:
                        "GamePixPluginMenu: GAMEPIX включён — сборки плагина появятся после перекомпиляции. "
                        + "Не забудьте выключить его перед обычным WebGL-билдом.",
                        disabledNote:
                        "GamePixPluginMenu: GAMEPIX выключен — сборки плагина исчезнут после перекомпиляции."),

                    // Не дефайн: односторонний ремонт двух файлов на диске.
                    // Галочка снята — обновление плагина стёрло ограничения,
                    // применение вернёт их обратно; когда всё на месте, делать
                    // нечего и строка гаснет.
                    new CustomOption(
                        ConstraintsId,
                        "Plugin Has defineConstraints",
                        "Стоят ли в asmdef-ах плагина ограничения по GAMEPIX. Обе сборки " +
                        "плагина обязаны быть ограничены: рантайм — чтобы не тянуть externs и " +
                        "авто-бутстрап в обычный WebGL-билд, редакторная — потому что " +
                        "Builder.cs использует ICSharpCode из рантайм-сборки. Обновление " +
                        "плагина эти ограничения стирает.",
                        required: true,
                        order: 202,
                        read: _ => GamePixAsmdefConstraints.AreInPlace()
                            ? ProjectOption.On
                            : ProjectOption.Off,
                        write: (value, context) =>
                        {
                            if (value == ProjectOption.On)
                                GamePixAsmdefConstraints.Restore(context);
                        },
                        availability: PluginPresent,
                        oneWay: true),

                    new DefineOption(
                        AnalyticsId,
                        AnalyticsDefineSymbol,
                        "Allow External Analytics (GA4)",
                        "Внешняя аналитика (GA4) в портальной сборке. По умолчанию выключено.",
                        required: true,
                        order: 203,
                        availability: state =>
                            ProjectOptionAvailability.RequireDefine(state, DefineSymbol, NeedsGamePix),

                        confirmation: new ProjectOptionConfirmation(
                            "GamePix",
                            "Enable external analytics (GA4) in the portal build?",
                            "Enable",
                            "Cancel"),
                        enabledNote:
                        "GamePixPluginMenu: внешняя аналитика включена — GA4 поднимется и в портальной сборке.",
                        disabledNote:
                        "GamePixPluginMenu: внешняя аналитика выключена — в портальной сборке GA4 не поднимается."),

                    new DefineOption(
                        AdDebugId,
                        AdDebugDefineSymbol,
                        "Show Ad Debug Overlay",
                        "Экранный журнал рекламы: в портальной сборке консоль браузера из " +
                        "чужого iframe не достать, а результат показа видеть надо. Оверлей " +
                        "появляется в игре сам, префаб не нужен. Полезен в любой сборке — " +
                        "вне портала он показывает Unavailable, и это тоже ответ.",
                        required: false,
                        order: 204,
                        enabledNote:
                        "GamePixPluginMenu: экранный журнал рекламы включён — оверлей появится в игре сам, префаб не нужен. "
                        + "Перед отправкой на ревью выключите.",
                        disabledNote: "GamePixPluginMenu: экранный журнал рекламы выключен."),

                    new DefineOption(
                        InjectSdkId,
                        InjectSdkDefineSymbol,
                        "Inject SDK Tag Into index.html",
                        "Вставлять ли тег SDK в index.html после билда. Нужно тест-тулкиту: " +
                        "без window.GamePix плагин молча не стартует. В сборку на ревью не " +
                        "идёт — там SDK подставляет издательский конвейер площадки.",
                        required: true,
                        order: 205,
                        availability: state =>
                            ProjectOptionAvailability.RequireDefine(state, DefineSymbol, NeedsGamePix),
                        enabledNote:
                        "GamePixPluginMenu: тег SDK будет вставляться в index.html — это для тест-тулкита. "
                        + "Перед сборкой на ревью выключите: площадка подставляет свой SDK сама.",
                        disabledNote:
                        "GamePixPluginMenu: тег SDK в index.html больше не вставляется (режим для .gpx на ревью)."),
                });
        }

        private static ProjectOptionAvailability PluginPresent(IProjectOptionState state) =>
            GamePixAsmdefConstraints.IsPluginVisible()
                ? ProjectOptionAvailability.Available
                : ProjectOptionAvailability.Blocked(PluginMissing);
    }
}
