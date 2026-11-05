using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LongLeap.BuildManager.Options
{
    // The project state as it is written NOW. This is what menu items see and
    // what Revert returns to.
    //
    // A snapshot, not live access: the define set is read once,
    // SerializedObjects are cached one per asset, and each option value is
    // read once. Create a NEW instance whenever fresh state is needed (window
    // focus, after Apply, on Revert) instead of keeping one for the window's
    // lifetime.
    public sealed class SavedProjectOptionState : IProjectReadContext, IProjectOptionState
    {
        private readonly Dictionary<string, SerializedObject> _assets =
            new Dictionary<string, SerializedObject>();

        private readonly Dictionary<string, string> _values = new Dictionary<string, string>();

        private ISet<string> _defines;

        public ISet<string> Defines => _defines ?? (_defines = ScriptingDefines.Read());

        public SerializedObject GetSerializedObject(string assetPath)
        {
            if (_assets.TryGetValue(assetPath, out SerializedObject cached))
                return cached;

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            SerializedObject serialized = asset == null ? null : new SerializedObject(asset);

            _assets[assetPath] = serialized;

            return serialized;
        }

        public bool IsDefineOn(string symbol) => Defines.Contains(symbol);

        public string GetValue(string optionId)
        {
            // Cache null too: "no asset" is an answer, and there is no point
            // asking again on every repaint.
            if (_values.TryGetValue(optionId, out string cached))
                return cached;

            string value = null;

            if (ProjectOptionRegistry.TryGetOption(optionId, out ProjectOption option))
            {
                try
                {
                    value = option.Read(this);
                }
                catch (Exception exception)
                {
                    // A broken option must not take the whole window down.
                    Debug.LogError($"Build options: reading '{optionId}' failed. {exception}");
                }
            }

            _values[optionId] = value;

            return value;
        }

        public bool TryGetOption(string optionId, out ProjectOption option) =>
            ProjectOptionRegistry.TryGetOption(optionId, out option);
    }
}
