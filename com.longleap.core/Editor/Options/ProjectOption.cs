using System;
using System.Collections.Generic;

namespace LongLeap.Core.Editor
{
    // Описание одной настройки проекта, влияющей на сборку: галочка «дефайн
    // включён», выбор среды в ассете, ремонт asmdef-ов вендорного плагина.
    //
    // Зачем таблица, если раньше были просто пункты меню: у MenuItem нет
    // тултипов (только menuItem/priority/secondaryPriority/validate/
    // editorModes), поэтому причина «зачем эта галочка» уезжала в Debug.Log
    // и читалась уже ПОСЛЕ переключения. Здесь причина лежит рядом с
    // настройкой в Tooltip, и окно показывает её до нажатия.
    //
    // Значение ЛЮБОЙ настройки — строка: On/Off для галочек, имя члена
    // перечисления для выбора. Одно представление на все виды даёт три вещи
    // бесплатно: отложенное состояние сериализуется как список пар строк и
    // переживает перекомпиляцию; «изменено ли» — это один string.Equals без
    // ветвления по виду настройки; среда хранится по ИМЕНИ, а не по индексу,
    // поэтому перестановка членов перечисления не переключит сервер молча
    // (та же причина, что у XsollaIdMenu.IndexOf).
    //
    // Описатель не имеет права держать ссылку на UnityEngine.Object — только
    // пути и делегаты. Иначе реестр протухал бы после обновления
    // AssetDatabase, а живёт он до перезагрузки домена.
    public abstract class ProjectOption
    {
        public const string On = "1";
        public const string Off = "0";

        // Ключ для пресетов и отложенного состояния. Живёт дольше подписи:
        // подпись можно переписать, Id менять нельзя — иначе пресеты
        // перестанут находить настройку.
        public string Id { get; }

        public string Label { get; }

        public string Tooltip { get; }

        // Обязательная или отладочная. Ровно то же деление, что у подменю
        // Required/Optional: без Required портальная сборка не соберётся или
        // не пройдёт ревью, Optional в готовую сборку попадать не должно.
        public bool Required { get; }

        // Совпадает с priority у MenuItem, чтобы порядок строк в окне был тем
        // же, что порядок пунктов в меню, и глаз не искал заново.
        public int Order { get; }

        // Настройку можно только ВКЛЮЧИТЬ. Ремонт asmdef-ов именно такой:
        // обратной операции не существует, и пункт меню гаснет, когда чинить
        // уже нечего.
        public bool OneWay { get; }

        // Видна ли настройка в окне. Скрытая по-прежнему доступна из меню и
        // по-прежнему участвует в пресетах.
        public bool VisibleInWindow { get; }

        protected ProjectOption(
            string id,
            string label,
            string tooltip,
            bool required,
            int order,
            bool oneWay = false,
            bool visibleInWindow = true)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("Option id must not be empty.", nameof(id));

            if (string.IsNullOrEmpty(label))
                throw new ArgumentException($"Option '{id}' must have a label.", nameof(label));

            Id = id;
            Label = label;
            Tooltip = tooltip;
            Required = required;
            Order = order;
            OneWay = oneWay;
            VisibleInWindow = visibleInWindow;
        }

        // Значение «выключено» для сквозных пресетов. null означает, что
        // естественного «выключено» нет — так у выбора среды: сбрасывать её
        // в никуда нельзя, поэтому пресет её и не тронет, если не назвал явно.
        public virtual string OffValue => Off;

        // null — галочка, иначе выбор из перечисленного.
        public virtual IReadOnlyList<ProjectOptionChoice> Choices => null;

        // Текущее значение. Читать только через контекст: он снимает набор
        // дефайнов один раз и кэширует по одному SerializedObject на ассет,
        // поэтому опросить все настройки разом стоит столько же, сколько одну.
        public abstract string Read(IProjectReadContext context);

        // Записать значение. Контекст НАКАПЛИВАЕТ изменения и пишет их одним
        // вызовом на весь Apply — см. ProjectWriteContext.Flush.
        public abstract void Write(string value, IProjectWriteContext context);

        // Доступна ли настройка. Считается против ПРЕДСТАВЛЕНИЯ состояния, а
        // не против проекта: меню передаёт сохранённое состояние и ведёт себя
        // как раньше, окно передаёт отложенное — и строка гаснет сразу, как
        // только сняли галочку выше, ещё до Apply.
        public virtual ProjectOptionAvailability GetAvailability(IProjectOptionState state) =>
            ProjectOptionAvailability.Available;

        // Спросить подтверждение перед записью. Нужно там, где галочка — не
        // удобство, а отступление от условий площадки.
        public virtual ProjectOptionConfirmation GetConfirmation(string newValue) => null;

        // Что сказать в журнал после применения. Здесь живут те самые
        // напоминания «выключите перед отправкой на ревью», которые раньше
        // печатал каждый пункт меню.
        public virtual string DescribeApplied(string value) => null;

        // Человекочитаемое значение для журнала и для подписи «(off -> on)».
        public virtual string DescribeValue(string value)
        {
            if (Choices == null)
                return value == On ? "on" : "off";

            foreach (ProjectOptionChoice choice in Choices)
            {
                if (choice.Value == value)
                    return choice.Label;
            }

            return string.IsNullOrEmpty(value) ? "<unset>" : value;
        }

        public override string ToString() => $"{Id} ({Label})";
    }

    // Один вариант выбора: значение — имя члена перечисления в ассете.
    public readonly struct ProjectOptionChoice
    {
        public string Value { get; }

        public string Label { get; }

        public string Tooltip { get; }

        public ProjectOptionChoice(string value, string label, string tooltip = null)
        {
            Value = value;
            Label = label;
            Tooltip = tooltip;
        }
    }
}
