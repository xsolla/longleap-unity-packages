using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LongLeap.Core.Editor
{
    // Накопитель изменений. Смысл его существования — в Flush: сколько бы
    // настроек ни поменяли за один Apply, символы компиляции запишутся ОДНИМ
    // вызовом, ассеты сохранятся ОДНИМ, и перезагрузка домена будет ОДНА.
    //
    // Раньше каждый пункт меню писал дефайны сам, поэтому смена конфигурации
    // на другую площадку стоила полдюжины перекомпиляций подряд.
    public sealed class ProjectWriteContext : IProjectWriteContext
    {
        private readonly ISet<string> _defines = ScriptingDefines.Read();

        private readonly Dictionary<string, SerializedObject> _assets =
            new Dictionary<string, SerializedObject>();

        private readonly List<string> _logs = new List<string>();
        private readonly List<string> _errors = new List<string>();

        private bool _refreshRequested;
        private bool _definesTouched;

        public void SetDefine(string symbol, bool on)
        {
            if (string.IsNullOrEmpty(symbol))
                return;

            if (on)
                _defines.Add(symbol);
            else
                _defines.Remove(symbol);

            _definesTouched = true;
        }

        public SerializedObject GetSerializedObject(string assetPath)
        {
            if (_assets.TryGetValue(assetPath, out SerializedObject cached))
                return cached;

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            SerializedObject serialized = asset == null ? null : new SerializedObject(asset);

            _assets[assetPath] = serialized;

            return serialized;
        }

        public void RequestAssetRefresh() => _refreshRequested = true;

        public void Log(string message)
        {
            if (!string.IsNullOrEmpty(message))
                _logs.Add(message);
        }

        public void LogError(string message)
        {
            if (!string.IsNullOrEmpty(message))
                _errors.Add(message);
        }

        // Порядок шагов важен: сначала файлы и ассеты, только потом дефайны.
        //
        // Ремонт asmdef-ов должен лежать на диске ПРАВИЛЬНЫМ до того, как
        // начнётся компиляция, которую запустит запись дефайна — иначе первая
        // компиляция пойдёт по старым ограничениям. Unity всё равно сольёт
        // оба повода в одну перезагрузку домена.
        public ProjectWriteResult Flush()
        {
            bool assetsChanged = false;

            foreach (SerializedObject asset in _assets.Values)
            {
                if (asset != null && asset.ApplyModifiedProperties())
                    assetsChanged = true;
            }

            if (assetsChanged)
                AssetDatabase.SaveAssets();

            if (_refreshRequested)
                AssetDatabase.Refresh();

            bool definesChanged = _definesTouched && ScriptingDefines.Write(_defines);

            // ProjectSettings.asset сам на диск не просится.
            if (definesChanged)
                AssetDatabase.SaveAssets();

            foreach (string error in _errors)
                Debug.LogError(error);

            foreach (string message in _logs)
                Debug.Log(message);

            return new ProjectWriteResult(definesChanged, assetsChanged, _refreshRequested);
        }
    }

    public readonly struct ProjectWriteResult
    {
        // Символы компиляции изменились, а значит будет перекомпиляция — и до
        // её конца собирать игру нельзя: код под #if соберётся по старому.
        public bool DefinesChanged { get; }

        public bool AssetsChanged { get; }

        public bool RefreshRequested { get; }

        public bool AnythingChanged => DefinesChanged || AssetsChanged || RefreshRequested;

        public ProjectWriteResult(bool definesChanged, bool assetsChanged, bool refreshRequested)
        {
            DefinesChanged = definesChanged;
            AssetsChanged = assetsChanged;
            RefreshRequested = refreshRequested;
        }
    }
}
