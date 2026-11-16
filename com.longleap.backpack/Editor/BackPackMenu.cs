using LongLeap.BuildManager.Options;
using UnityEditor;

namespace LongLeap.Backpack.Editor
{
    // The BackPack environment menu.
    //
    // Three checked items rather than one checkbox — for the same reason as the
    // Xsolla ID environment: it shows exactly where events go, with no guessing
    // what an unchecked box means. Local is separate from Development on purpose:
    // they are DIFFERENT servers, see BackPackEnvironment.
    public static class BackPackMenu
    {
        private const string LocalMenuPath = "BackPack/Required/Environment/Local";
        private const string DevelopmentMenuPath = "BackPack/Required/Environment/Development";
        private const string ProductionMenuPath = "BackPack/Required/Environment/Production";

        [MenuItem(LocalMenuPath, false, 400)]
        private static void SelectLocal() =>
            ProjectOptionMenu.Select(BackPackOptions.EnvironmentId, BackPackOptions.LocalName);

        [MenuItem(LocalMenuPath, true)]
        private static bool SelectLocalValidate() =>
            ProjectOptionMenu.Validate(
                LocalMenuPath, BackPackOptions.EnvironmentId, BackPackOptions.LocalName);

        [MenuItem(DevelopmentMenuPath, false, 401)]
        private static void SelectDevelopment() =>
            ProjectOptionMenu.Select(BackPackOptions.EnvironmentId, BackPackOptions.DevelopmentName);

        [MenuItem(DevelopmentMenuPath, true)]
        private static bool SelectDevelopmentValidate() =>
            ProjectOptionMenu.Validate(
                DevelopmentMenuPath, BackPackOptions.EnvironmentId, BackPackOptions.DevelopmentName);

        [MenuItem(ProductionMenuPath, false, 402)]
        private static void SelectProduction() =>
            ProjectOptionMenu.Select(BackPackOptions.EnvironmentId, BackPackOptions.ProductionName);

        [MenuItem(ProductionMenuPath, true)]
        private static bool SelectProductionValidate() =>
            ProjectOptionMenu.Validate(
                ProductionMenuPath, BackPackOptions.EnvironmentId, BackPackOptions.ProductionName);

        [MenuItem("BackPack/Select Config Asset", false, 403)]
        private static void SelectConfig() => BackPackOptions.SelectConfigAsset();
    }
}
