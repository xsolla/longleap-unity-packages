using System.Collections.Generic;
using LongLeap.Core.Editor;

namespace LongLeap.BuildManager.Editor
{
    // The saved state plus the unwritten edits.
    //
    // This is the view the window computes availability against, and that is
    // why a dependent row (a debug panel, say) is disabled the moment the
    // feature it depends on is unchecked, before Apply. Menu items meanwhile
    // compute availability against the saved state.
    internal sealed class StagedProjectOptionState : IStagedOptionState
    {
        private readonly SavedProjectOptionState _saved;
        private readonly Dictionary<string, string> _pending;

        public StagedProjectOptionState(
            SavedProjectOptionState saved, Dictionary<string, string> pending)
        {
            _saved = saved;
            _pending = pending;
        }

        public IReadOnlyDictionary<string, string> Pending => _pending;

        public bool IsDirty => _pending.Count > 0;

        public bool IsOptionDirty(string optionId) => _pending.ContainsKey(optionId);

        public string GetValue(string optionId) =>
            _pending.TryGetValue(optionId, out string pending) ? pending : _saved.GetValue(optionId);

        public string GetSavedValue(string optionId) => _saved.GetValue(optionId);

        public bool IsDefineOn(string symbol)
        {
            // If a registered option stands behind the symbol, answer from the
            // staged value. Unregistered symbols (someone may set them by hand
            // in Player Settings) are read from the saved state: there is
            // nothing else known about them.
            if (ProjectOptionRegistry.TryGetDefineOption(symbol, out DefineOption option))
            {
                string value = GetValue(option.Id);

                if (value != null)
                    return value == ProjectOption.On;
            }

            return _saved.IsDefineOn(symbol);
        }

        public bool TryGetOption(string optionId, out ProjectOption option) =>
            ProjectOptionRegistry.TryGetOption(optionId, out option);

        public void Set(string optionId, string value)
        {
            if (!ProjectOptionRegistry.TryGetOption(optionId, out ProjectOption option))
                return;

            Stage(optionId, value);

            if (!(option is DefineOption define) || value == ProjectOption.On)
                return;

            // Cascade: with a feature off, its dependent defines (its debug
            // mode, say) have nothing left to work on. This is NOT the same as
            // disabling the row: the symbol really leaves the set, exactly as
            // DefineOption.Write does when the option is written from a menu.
            //
            // Walked with a queue and a visited set rather than recursion, so
            // two descriptors that cascade into each other cannot overflow the
            // stack.
            var visited = new HashSet<string> { define.Id };
            var queue = new Queue<DefineOption>();
            queue.Enqueue(define);

            while (queue.Count > 0)
            {
                foreach (string dependent in queue.Dequeue().CascadeOffSymbols)
                {
                    if (!ProjectOptionRegistry.TryGetDefineOption(dependent, out DefineOption cascaded))
                        continue;

                    if (!visited.Add(cascaded.Id))
                        continue;

                    Stage(cascaded.Id, ProjectOption.Off);
                    queue.Enqueue(cascaded);
                }
            }
        }

        // A value equal to the saved one is NOT stored. Because of that, "is it
        // changed" is just the presence of the key, and clicking a checkbox
        // twice clears the highlight by itself.
        private void Stage(string optionId, string value)
        {
            if (value == _saved.GetValue(optionId))
                _pending.Remove(optionId);
            else
                _pending[optionId] = value;
        }

        public void Clear(string optionId) => _pending.Remove(optionId);

        public void ClearAll() => _pending.Clear();
    }
}
