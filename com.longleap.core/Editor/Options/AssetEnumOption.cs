using System;
using System.Collections.Generic;
using UnityEditor;

namespace LongLeap.Core.Editor
{
    // A choice from an enum stored as a field in an asset.
    //
    // Values such as a server address or an environment are DATA and belong
    // in an asset with the rest of the data, not in Player Settings, so this
    // option writes to an asset rather than to defines.
    //
    // The asset is accessed through SerializedObject and the field NAME,
    // without a typed reference: the editor assembly that toggles defines
    // must not depend on a runtime assembly that those defines may exclude.
    // As a bonus, the option still works when the runtime assembly is
    // disabled by its defineConstraints.
    public sealed class AssetEnumOption : ProjectOption
    {
        private readonly Func<IProjectOptionState, ProjectOptionAvailability> _availability;
        private readonly IReadOnlyList<ProjectOptionChoice> _choices;

        public string AssetPath { get; }

        public string PropertyPath { get; }

        // What to do after writing, typically warn that the selected
        // environment has empty fields.
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

        // An environment has no "off" value, so an exhaustive preset leaves it
        // alone unless it names it.
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

            // Look up by NAME rather than writing the index: reordering enum
            // members would otherwise silently switch the value.
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

            // No asset, nothing to switch.
            //
            // Uses the already-read value instead of loading the asset again:
            // Read returns null exactly in this case, and the state caches it.
            // Otherwise every repaint would create a new SerializedObject for
            // each such option.
            return state?.GetValue(Id) != null
                ? ProjectOptionAvailability.Available
                : ProjectOptionAvailability.Blocked($"{AssetPath} not found — nothing to switch.");
        }

        private SerializedProperty FindProperty(SerializedObject asset) =>
            asset?.FindProperty(PropertyPath);
    }
}
