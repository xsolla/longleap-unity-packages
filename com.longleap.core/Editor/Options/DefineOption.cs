using System;
using System.Collections.Generic;

namespace LongLeap.Core.Editor
{
    // Галочка, за которой стоит один символ компиляции.
    public sealed class DefineOption : ProjectOption
    {
        private readonly Func<IProjectOptionState, ProjectOptionAvailability> _availability;

        public string Symbol { get; }

        // Символы, которые уходят ВМЕСТЕ с этим, когда его выключают.
        //
        // Это НЕ то же самое, что GetAvailability, и путать их нельзя —
        // в проекте есть оба поведения, и они разные:
        //
        //   XSOLLA_ID выключили  -> XSOLLA_ID_DEBUG УБИРАЕТСЯ из списка
        //                           (XsollaIdMenu.SetDefine, «отлаживать
        //                           нечего, если самого входа нет»);
        //   GAMEPIX выключили    -> GAMEPIX_ALLOW_ANALYTICS и остальные ОСТАЮТСЯ
        //                           в списке, просто гаснут пункты меню
        //                           (ValidateToggleAllowAnalytics).
        //
        // Поэтому общего правила «недоступна — значит выключить» здесь нет:
        // оно тихо изменило бы поведение GamePix. Каскад объявляет только тот,
        // кому он нужен.
        public IReadOnlyList<string> CascadeOffSymbols { get; }

        public ProjectOptionConfirmation Confirmation { get; }

        public string EnabledNote { get; }

        public string DisabledNote { get; }

        public DefineOption(
            string id,
            string symbol,
            string label,
            string tooltip,
            bool required,
            int order,
            Func<IProjectOptionState, ProjectOptionAvailability> availability = null,
            IReadOnlyList<string> cascadeOffSymbols = null,
            ProjectOptionConfirmation confirmation = null,
            string enabledNote = null,
            string disabledNote = null,
            bool visibleInWindow = true)
            : base(id, label, tooltip, required, order, oneWay: false, visibleInWindow: visibleInWindow)
        {
            if (string.IsNullOrEmpty(symbol))
                throw new ArgumentException($"Option '{id}' must name a define symbol.", nameof(symbol));

            Symbol = symbol;
            _availability = availability;
            CascadeOffSymbols = cascadeOffSymbols ?? Array.Empty<string>();
            Confirmation = confirmation;
            EnabledNote = enabledNote;
            DisabledNote = disabledNote;
        }

        public override string Read(IProjectReadContext context) =>
            context != null && context.Defines.Contains(Symbol) ? On : Off;

        public override void Write(string value, IProjectWriteContext context)
        {
            if (context == null)
                return;

            bool on = value == On;

            context.SetDefine(Symbol, on);

            if (on)
                return;

            foreach (string dependent in CascadeOffSymbols)
                context.SetDefine(dependent, false);
        }

        public override ProjectOptionAvailability GetAvailability(IProjectOptionState state) =>
            _availability?.Invoke(state) ?? ProjectOptionAvailability.Available;

        // Спрашиваем только при ВКЛЮЧЕНИИ: выключить отступление от условий
        // площадки можно и без разрешения.
        public override ProjectOptionConfirmation GetConfirmation(string newValue) =>
            newValue == On ? Confirmation : null;

        public override string DescribeApplied(string value) =>
            value == On ? EnabledNote : DisabledNote;
    }
}
