using System;
using System.Collections.Generic;
using UnityEditor;

namespace LongLeap.Core.Editor
{
    // Выбор из перечисления, лежащего полем в ассете.
    //
    // Адрес сервера и среда — это ДАННЫЕ, и лежать они должны там же, где
    // остальные данные, а не в Player Settings; ровно это объясняет
    // BackPackConfigSO в своём заголовке. Поэтому такая настройка пишется не
    // в дефайны, а в ассет.
    //
    // К ассету обращаемся через SerializedObject и поле ПО ИМЕНИ, без
    // типизированной ссылки. Причина та же, по которой так сделан
    // XsollaIdMenu: редакторной сборке, которая включает дефайны, нельзя
    // зависеть от рантайм-сборки, иначе появится риск, что она исчезнет
    // вместе с ней. Побочная выгода — описатель работает и тогда, когда
    // рантайм-сборка выключена своим defineConstraints.
    public sealed class AssetEnumOption : ProjectOption
    {
        private readonly Func<IProjectOptionState, ProjectOptionAvailability> _availability;
        private readonly IReadOnlyList<ProjectOptionChoice> _choices;

        public string AssetPath { get; }

        public string PropertyPath { get; }

        // Что сделать после записи: обычно предупредить, что у выбранной среды
        // не заполнены поля. Аналог XsollaIdMenu.WarnIfIncomplete.
        public Action<string, IProjectWriteContext> OnApplied { get; }

        public AssetEnumOption(
            string id,
            string assetPath,
            string propertyPath,
            string label,
            string tooltip,
            bool required,
            int order,
            IReadOnlyList<ProjectOptionChoice> choices,
            Func<IProjectOptionState, ProjectOptionAvailability> availability = null,
            Action<string, IProjectWriteContext> onApplied = null,
            bool visibleInWindow = true)
            : base(id, label, tooltip, required, order, oneWay: false, visibleInWindow: visibleInWindow)
        {
            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException($"Option '{id}' must name an asset path.", nameof(assetPath));

            if (string.IsNullOrEmpty(propertyPath))
                throw new ArgumentException($"Option '{id}' must name a property.", nameof(propertyPath));

            if (choices == null || choices.Count == 0)
                throw new ArgumentException($"Option '{id}' must offer at least one choice.", nameof(choices));

            AssetPath = assetPath;
            PropertyPath = propertyPath;
            _choices = choices;
            _availability = availability;
            OnApplied = onApplied;
        }

        // Сбросить среду «в выключено» нельзя — такого значения у неё нет.
        // Поэтому сквозной пресет её не тронет, если не назовёт явно.
        public override string OffValue => null;

        public override IReadOnlyList<ProjectOptionChoice> Choices => _choices;

        public override string Read(IProjectReadContext context)
        {
            SerializedProperty property = FindProperty(context?.GetSerializedObject(AssetPath));

            if (property == null)
                return null;

            int index = property.enumValueIndex;
            string[] names = property.enumNames;

            return index >= 0 && index < names.Length ? names[index] : null;
        }

        public override void Write(string value, IProjectWriteContext context)
        {
            if (context == null)
                return;

            SerializedObject asset = context.GetSerializedObject(AssetPath);
            SerializedProperty property = FindProperty(asset);

            if (property == null)
            {
                context.LogError(
                    $"{Label}: {AssetPath} not found, or it has no {PropertyPath} field.");
                return;
            }

            // Ищем по ИМЕНИ, а не пишем номер: перестановка членов
            // перечисления иначе тихо переключила бы среду. Та же причина, что
            // у XsollaIdMenu.IndexOf.
            int index = Array.IndexOf(property.enumNames, value);

            if (index < 0)
            {
                context.LogError(
                    $"{Label}: the enum behind {PropertyPath} has no member '{value}' — it was renamed.");
                return;
            }

            if (property.enumValueIndex != index)
                property.enumValueIndex = index;

            OnApplied?.Invoke(value, context);
        }

        public override ProjectOptionAvailability GetAvailability(IProjectOptionState state)
        {
            if (_availability != null)
                return _availability(state);

            // Ассета нет — переключать нечего. Ровно то же говорил
            // XsollaIdMenu.ValidateEnvironment, возвращая environment != null.
            //
            // Спрашиваем готовое значение, а не грузим ассет заново: Read
            // отдаёт null ровно в этом случае, а состояние уже держит его в
            // кэше. Иначе на каждую перерисовку окна создавался бы новый
            // SerializedObject на каждую такую настройку.
            return state?.GetValue(Id) != null
                ? ProjectOptionAvailability.Available
                : ProjectOptionAvailability.Blocked($"{AssetPath} not found — nothing to switch.");
        }

        private SerializedProperty FindProperty(SerializedObject asset) =>
            asset?.FindProperty(PropertyPath);
    }
}
