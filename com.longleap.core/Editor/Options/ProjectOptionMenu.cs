using UnityEditor;

namespace LongLeap.Core.Editor
{
    // Binds the option table to menu items.
    //
    // [MenuItem] attributes cannot be generated: the path is a compile-time
    // constant and each item needs its own method. So each menu keeps a pair
    // of one-line methods per option, while all the logic (confirmation,
    // cascade, write, logging) lives here once and matches what the window
    // does.
    public static class ProjectOptionMenu
    {
        // Toggle. Returns whether the menu item is enabled.
        public static bool Validate(string menuPath, string optionId)
        {
            if (!ProjectOptionRegistry.TryGetOption(optionId, out ProjectOption option))
            {
                Menu.SetChecked(menuPath, false);
                return false;
            }

            var state = new SavedProjectOptionState();
            bool on = state.GetValue(optionId) == ProjectOption.On;

            Menu.SetChecked(menuPath, on);

            if (!option.GetAvailability(state).IsAvailable)
                return false;

            // A one-way option is enabled only while there is something to do:
            // once the repair is applied, the item is disabled.
            return !option.OneWay || !on;
        }

        // One value out of several: one menu item per value, with a check mark
        // on the active one. Separate items instead of one checkbox make it
        // clear which value is selected (e.g. which environment sign-in uses).
        public static bool Validate(string menuPath, string optionId, string expectedValue)
        {
            if (!ProjectOptionRegistry.TryGetOption(optionId, out ProjectOption option))
            {
                Menu.SetChecked(menuPath, false);
                return false;
            }

            var state = new SavedProjectOptionState();

            Menu.SetChecked(menuPath, state.GetValue(optionId) == expectedValue);

            return option.GetAvailability(state).IsAvailable;
        }

        public static void Toggle(string optionId)
        {
            if (!ProjectOptionRegistry.TryGetOption(optionId, out ProjectOption option))
                return;

            var state = new SavedProjectOptionState();
            bool on = state.GetValue(optionId) == ProjectOption.On;

            if (on && option.OneWay)
                return;

            Set(optionId, on ? ProjectOption.Off : ProjectOption.On);
        }

        public static void Select(string optionId, string value) => Set(optionId, value);

        private static void Set(string optionId, string value)
        {
            if (!ProjectOptionRegistry.TryGetOption(optionId, out ProjectOption option))
                return;

            var state = new SavedProjectOptionState();
            string current = state.GetValue(optionId);

            // Nothing changed: do not write and do not log.
            if (current == value)
                return;

            ProjectOptionConfirmation confirmation = option.GetConfirmation(value);

            if (confirmation != null && !EditorUtility.DisplayDialog(
                    confirmation.Title,
                    confirmation.Message,
                    confirmation.Confirm,
                    confirmation.Cancel))
                return;

            var context = new ProjectWriteContext();

            option.Write(value, context);
            context.Log(option.DescribeApplied(value));

            context.Flush();
        }
    }
}
