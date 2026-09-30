#if UNITY_EDITOR
using System.Collections.Generic;
using LongLeap.Core.Editor;

namespace LongLeap.XsollaId.Editor
{
    internal sealed class XsollaIdOptions : IProjectOptionSectionProvider
    {
        public const string EnabledId = "xsollaid.enabled";
        public const string DebugId = "xsollaid.debug";

        private const string Define = "XSOLLA_ID";
        private const string DebugDefine = "XSOLLA_ID_DEBUG";

        public IEnumerable<ProjectOptionSection> GetSections()
        {
            yield return new ProjectOptionSection(
                "xsollaid",
                "Xsolla ID",
                20,
                new ProjectOption[]
                {
                    new DefineOption(
                        EnabledId,
                        Define,
                        "Sign-in Enabled (XSOLLA_ID)",
                        "Вход через Xsolla ID. Сборка LongLeap.XsollaId.Web ограничена этим " +
                        "дефайном, то есть без него её в проекте нет вовсе и вход не " +
                        "соберётся. Выключение убирает и отладочную панель: отлаживать " +
                        "нечего, если самого входа нет.",
                        required: true,
                        order: 100,

                        // Убирается ВМЕСТЕ со входом, а не просто гаснет —
                        // ровно как было в XsollaIdMenu.SetDefine.
                        cascadeOffSymbols: new[] { DebugDefine },
                        enabledNote:
                        "Xsolla ID: sign-in enabled (XSOLLA_ID added). Do not forget to fill in clientId in "
                        + XsollaIdConfigAccess.ConfigAssetPath,
                        disabledNote:
                        "Xsolla ID: sign-in disabled (XSOLLA_ID removed). The build will compile with no network calls to id.xsolla.com."),

                    new DefineOption(
                        DebugId,
                        DebugDefine,
                        "Debug Panel (on-screen)",
                        "Отладочный вход с клавиатуры: F9 — войти, F10 — выйти, F11 — " +
                        "состояние. Работает только в WebGL-сборке. В сборку на площадку " +
                        "попадать не должен.",
                        required: false,
                        order: 101,
                        availability: state => ProjectOptionAvailability.RequireDefine(
                            state, Define, "Sign-in must be on — there is nothing to debug otherwise."),
                        enabledNote:
                        "Xsolla ID: the debug trigger is on — F9 sign in, F10 sign out, F11 state. "
                        + "Works only in a WebGL build; turn it off before submitting to the portal.",
                        disabledNote: "Xsolla ID: the debug sign-in trigger is off."),
                },
                actions: new[]
                {
                    new ProjectOptionAction(
                        "Select Config Asset",
                        $"Выделить {XsollaIdConfigAccess.ConfigAssetPath} в проекте.",
                        102,
                        XsollaIdConfigAccess.SelectConfigAsset),
                });
        }
    }
}
#endif
