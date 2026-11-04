using System.Collections.Generic;
using LongLeap.Core.Editor;

namespace LongLeap.BuildManager.Editor
{
    // The three ways the game is shipped.
    //
    // A preset is a SET OF VALUES, so it inevitably knows about several packages
    // at once: "GAMEPIX on AND XSOLLA_ID off" is something no single package can
    // say about itself. But it addresses options by string Id, so no references
    // between assemblies appear because of it, and unknown Ids are simply
    // skipped: in a project without the GamePix package the preset applies the
    // rest.
    //
    // A project adds its own presets by implementing IProjectOptionPresetProvider
    // in any of its editor assemblies: the interface lives in Core, not here.
    internal sealed class DefaultPresets : IProjectOptionPresetProvider
    {
        public IEnumerable<ProjectOptionPreset> GetPresets()
        {
            // Portal build.
            //
            // GAMEPIX_ALLOW_ANALYTICS is off in ALL presets, this one included:
            // a preset does not turn it on with one click, bypassing the
            // confirmation dialog.
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

                    // 127.0.0.1 is unreachable from a third-party iframe on the portal.
                    { "backpack.environment", "Production" },
                },
                "Portal build: the GamePix plugin is on and nothing debug-only is enabled.");

            // Self-hosted HTML5 build with Xsolla ID sign-in.
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
