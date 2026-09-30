using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LongLeap.Core.Editor
{
    // Состояние проекта, как оно записано СЕЙЧАС. Это то, что видят пункты
    // меню, и то, к чему возвращает Revert.
    //
    // Снимок, а не сквозные обращения к проекту: набор дефайнов читается один
    // раз, SerializedObject кэшируется по одному на ассет, значение каждой
    // настройки — по одному разу. Поэтому объект надо СОЗДАВАТЬ ЗАНОВО, когда
    // нужно свежее состояние (при фокусе окна, после Apply, на Revert), а не
    // держать один на всё время жизни окна.
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
            // null тоже кэшируем: «ассета нет» — это ответ, и переспрашивать
            // его на каждую перерисовку незачем.
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
                    // Сломанный описатель не должен ронять всё окно.
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
