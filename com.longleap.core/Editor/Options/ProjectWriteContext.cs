using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LongLeap.Core.Editor
{
    // Change accumulator. The point is Flush: however many options change in
    // one Apply, defines are written in ONE call, assets are saved ONCE, and
    // there is ONE domain reload, so switching to another platform
    // configuration costs one recompilation instead of several.
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

        // Step order matters: files and assets first, defines last.
        //
        // Files repaired on disk (e.g. asmdefs) must be CORRECT before the
        // compilation triggered by the define change starts, otherwise the
        // first compilation would use the old constraints. Unity merges both
        // triggers into a single domain reload.
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

            // ProjectSettings.asset is not saved to disk on its own.
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
        // Defines changed, so a recompilation follows. Do not build until it
        // finishes: code under #if would be compiled the old way.
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
