using System;

namespace LongLeap.Core.Editor
{
    // Доступна ли настройка, и если нет — ПОЧЕМУ. Причина обязательна:
    // погашенный пункт меню без объяснения приходилось разгадывать чтением
    // исходников (почему не включается GAMEPIX — потому что нет папки
    // Assets/Plugins/GamePix, но узнать это было негде).
    public readonly struct ProjectOptionAvailability
    {
        public bool IsAvailable { get; }

        // null, когда доступна.
        public string Reason { get; }

        private ProjectOptionAvailability(bool isAvailable, string reason)
        {
            IsAvailable = isAvailable;
            Reason = reason;
        }

        public static ProjectOptionAvailability Available => new ProjectOptionAvailability(true, null);

        public static ProjectOptionAvailability Blocked(string reason)
        {
            if (string.IsNullOrEmpty(reason))
                throw new ArgumentException("A blocked option must explain why.", nameof(reason));

            return new ProjectOptionAvailability(false, reason);
        }

        // Самый частый случай: настройка имеет смысл только при включённом
        // другом дефайне. Именно так GAMEPIX гасит три своих спутника — но
        // ТОЛЬКО гасит: значения из списка дефайнов при этом не убираются,
        // ровно как было в ValidateToggleInjectSdk. Убирать зависимые дефайны
        // умеет DefineOption.CascadeOffSymbols, и это другое поведение —
        // единственный его носитель XSOLLA_ID.
        public static ProjectOptionAvailability RequireDefine(
            IProjectOptionState state, string symbol, string reason) =>
            state != null && state.IsDefineOn(symbol) ? Available : Blocked(reason);
    }

    // Диалог перед записью значения. Отмена отменяет ровно эту настройку, а
    // не весь Apply: отказ от одного юридического согласия не повод терять
    // остальные подготовленные изменения.
    public sealed class ProjectOptionConfirmation
    {
        public string Title { get; }

        public string Message { get; }

        public string Confirm { get; }

        public string Cancel { get; }

        public ProjectOptionConfirmation(string title, string message, string confirm, string cancel)
        {
            Title = title;
            Message = message;
            Confirm = confirm;
            Cancel = cancel;
        }
    }
}
