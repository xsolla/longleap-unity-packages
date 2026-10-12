using System.Collections.Generic;
using LongLeap.Core.Editor;

namespace LongLeap.BuildManager.Editor
{
    // Три способа, которыми игра уезжает наружу.
    //
    // Пресет — это НАБОР ЗНАЧЕНИЙ, а значит он неизбежно знает про несколько
    // пакетов сразу: «GAMEPIX включён И XSOLLA_ID выключен» ни один пакет про
    // себя сказать не может. Но адресует он настройки по строковому Id,
    // поэтому ссылок между сборками из-за этого не появляется, а незнакомые Id
    // просто пропускаются — в проекте без пакета GamePix пресет применит
    // остальное.
    //
    // Свои пресеты проект добавляет, реализовав IProjectOptionPresetProvider в
    // любой своей редакторной сборке: интерфейс лежит в Core, не здесь.
    internal sealed class DefaultPresets : IProjectOptionPresetProvider
    {
        public IEnumerable<ProjectOptionPreset> GetPresets()
        {
            // Портальная сборка.
            //
            // GAMEPIX_ALLOW_ANALYTICS выключен во ВСЕХ пресетах, включая этот:
            // пресет не взводит его одним нажатием, минуя диалог подтверждения.
            yield return new ProjectOptionPreset(
                "gamepix",
                "GamePix",
                10,
                new Dictionary<string, string>
                {
                    { "gamepix.enabled", ProjectOption.On },
                    { "gamepix.allow_analytics", ProjectOption.Off },
                    { "gamepix.ad_debug", ProjectOption.Off },

                    // On for the portal too: the sign-in itself comes from the
                    // GamePix SDK (XsollaIdAuthGamePix), but cloud saves and
                    // leaderboards on our server live in the XSOLLA_ID assembly.
                    { "xsollaid.enabled", ProjectOption.On },
                    { "xsollaid.debug", ProjectOption.Off },
                    { "devtools.debug", ProjectOption.Off },

                    // 127.0.0.1 из чужого iframe на площадке недостижим.
                    { "backpack.environment", "Production" },
                },
                "Portal build: the GamePix plugin is on and nothing debug-only is enabled.");

            // Самостоятельная HTML5-сборка со входом через Xsolla ID.
            yield return new ProjectOptionPreset(
                "xsollaid",
                "Xsolla ID",
                20,
                new Dictionary<string, string>
                {
                    { "xsollaid.enabled", ProjectOption.On },
                    { "xsollaid.debug", ProjectOption.Off },
                    { "gamepix.enabled", ProjectOption.Off },
                    { "gamepix.allow_analytics", ProjectOption.Off },
                    { "gamepix.ad_debug", ProjectOption.Off },
                    { "devtools.debug", ProjectOption.Off },
                    { "backpack.environment", "Production" },
                },
                "Self-hosted HTML5 build with Xsolla ID sign-in.");

            // Local development: Xsolla ID sign-in against the RewardService
            // running on this machine (127.0.0.1). Everything not named here,
            // GamePix and the debug panels included, is switched off.
            yield return new ProjectOptionPreset(
                "local",
                "Local",
                30,
                new Dictionary<string, string>
                {
                    { "xsollaid.enabled", ProjectOption.On },
                    { "backpack.environment", "Local" },
                },
                "Local WebGL build: Xsolla ID sign-in, BackPack and cloud saves on the local server, no portal, no debug panels.");
        }
    }
}
