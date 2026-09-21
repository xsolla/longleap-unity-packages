using System;
using System.Collections.Generic;

namespace LongLeap.Core.Editor
{
    // Блок настроек одного пакета: заголовок плюс список настроек и действий.
    //
    // Деление Required/Optional здесь НЕ объявляется — оно выводится из самих
    // настроек. Раньше это деление существовало только как кусок строки в
    // пути меню («GamePix/Required/...»), то есть не было данными вообще.
    public sealed class ProjectOptionSection
    {
        public string Id { get; }

        public string Title { get; }

        // Необязательная строка под заголовком.
        public string Description { get; }

        public int Order { get; }

        public IReadOnlyList<ProjectOption> Options { get; }

        // То, что состоянием проекта не является: «запаковать zip», «выделить
        // ассет конфига», «вернуть ограничения в asmdef-ы». Это действия, и
        // откладывать их бессмысленно — у них нет отложенного значения.
        public IReadOnlyList<ProjectOptionAction> Actions { get; }

        public ProjectOptionSection(
            string id,
            string title,
            int order,
            IReadOnlyList<ProjectOption> options,
            string description = null,
            IReadOnlyList<ProjectOptionAction> actions = null)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("Section id must not be empty.", nameof(id));

            if (string.IsNullOrEmpty(title))
                throw new ArgumentException($"Section '{id}' must have a title.", nameof(title));

            Id = id;
            Title = title;
            Order = order;
            Options = options ?? Array.Empty<ProjectOption>();
            Description = description;
            Actions = actions ?? Array.Empty<ProjectOptionAction>();
        }

        public override string ToString() => $"{Id} ({Title})";
    }

    // Кнопка, а не галочка.
    public sealed class ProjectOptionAction
    {
        private readonly Func<string> _disabledReason;

        public string Label { get; }

        public string Tooltip { get; }

        public int Order { get; }

        public Action Invoke { get; }

        public ProjectOptionAction(
            string label,
            string tooltip,
            int order,
            Action invoke,
            Func<string> disabledReason = null)
        {
            Label = label;
            Tooltip = tooltip;
            Order = order;
            Invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
            _disabledReason = disabledReason;
        }

        // null — кнопка активна. Иначе причина, по которой она погашена.
        public string GetDisabledReason() => _disabledReason?.Invoke();
    }

    // Пакет объявляет свои настройки, реализовав этот интерфейс. Реестр найдёт
    // реализацию сам, см. ProjectOptionRegistry.
    public interface IProjectOptionSectionProvider
    {
        IEnumerable<ProjectOptionSection> GetSections();
    }
}
