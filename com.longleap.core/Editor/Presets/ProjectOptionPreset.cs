using System;
using System.Collections.Generic;

namespace LongLeap.Core.Editor
{
    // Названный набор значений: «это сборка для GamePix», «это самостоятельный
    // HTML5 с входом через Xsolla ID».
    //
    // Пресет адресует настройки ПО СТРОКОВОМУ Id, а не по типу. Из-за этого он
    // может назвать настройки из разных пакетов, не заводя между ними ссылок —
    // и именно поэтому пресет способен сказать «GAMEPIX включён И XSOLLA_ID
    // выключен», чего ни один пакет про себя сказать не может.
    //
    // Неизвестные Id пропускаются с сообщением в журнал, а не с ошибкой: в
    // проекте без пакета GamePix пресет обязан просто применить остальное.
    public sealed class ProjectOptionPreset
    {
        public string Id { get; }

        public string Label { get; }

        public string Tooltip { get; }

        public int Order { get; }

        public IReadOnlyDictionary<string, string> Values { get; }

        // Не названные явно настройки уводятся в своё «выключено».
        //
        // Это то, что позволяет пресету «Pure» означать «никаких дефайнов» без
        // перечисления символов, которых в этом проекте может и не быть, и
        // заставляет переход GamePix -> Xsolla ID действительно выключить
        // GAMEPIX, а не оставить его включённым.
        //
        // Не трогает настройки без естественного «выключено» (выбор среды) и
        // односторонние (ремонт asmdef-ов): сбрасывать их некуда, да и нечем.
        public bool Exhaustive { get; }

        public ProjectOptionPreset(
            string id,
            string label,
            int order,
            IReadOnlyDictionary<string, string> values,
            string tooltip = null,
            bool exhaustive = true)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("Preset id must not be empty.", nameof(id));

            if (string.IsNullOrEmpty(label))
                throw new ArgumentException($"Preset '{id}' must have a label.", nameof(label));

            Id = id;
            Label = label;
            Order = order;
            Values = values ?? new Dictionary<string, string>();
            Tooltip = tooltip;
            Exhaustive = exhaustive;
        }

        public override string ToString() => $"{Id} ({Label})";
    }

    public interface IProjectOptionPresetProvider
    {
        IEnumerable<ProjectOptionPreset> GetPresets();
    }
}
