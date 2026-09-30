using LongLeap.Core.Editor;
using UnityEditor;

namespace BloodRedSnow.BackPack.Editor
{
    // Меню выбора среды BackPack.
    //
    // Это то меню, которое BackPackConfigSO обещал в своём комментарии
    // («Переключать удобнее из меню BackPack/Required/Environment»), но
    // которого до сих пор не существовало: среду правили руками в инспекторе.
    //
    // Три пункта с галочкой, а не один чекбокс — по той же причине, что у
    // среды Xsolla ID: так видно, куда именно уходят события, и не приходится
    // догадываться, что значит снятая галочка. Local отдельно от Development
    // намеренно: это РАЗНЫЕ сервера, см. BackPackEnvironment.
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
