using UnityEditor;

namespace LongLeap.Core.Editor
{
    // Связка таблицы настроек с пунктами меню.
    //
    // Сами атрибуты [MenuItem] генерировать нельзя: путь у них — константа
    // времени компиляции, и метод нужен на каждый пункт. Поэтому в каждом меню
    // остаётся по паре методов на настройку, но тело у них теперь одна строка,
    // а вся логика — подтверждение, каскад, запись, журнал — живёт здесь в
    // одном экземпляре и совпадает с тем, что делает окно.
    public static class ProjectOptionMenu
    {
        // Галочка. Возвращает, активен ли пункт.
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

            // Односторонняя настройка активна только пока делать есть что —
            // так вёл себя пункт ремонта asmdef-ов: стоит ограничениям встать
            // на место, и он гаснет.
            return !option.OneWay || !on;
        }

        // Выбор одного значения из нескольких: по пункту на значение, галочка
        // стоит у активного. Так сделана среда у Xsolla ID — два пункта, а не
        // один чекбокс, чтобы было видно, куда именно ходит вход, и не
        // приходилось догадываться, что значит снятая галочка.
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

            // Ничего не менялось — не пишем и не шумим в журнал. Прежний
            // SetDefine ради этого возвращал bool.
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
