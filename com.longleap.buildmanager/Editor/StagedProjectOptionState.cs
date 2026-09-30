using System.Collections.Generic;
using LongLeap.Core.Editor;

namespace LongLeap.BuildManager.Editor
{
    // Сохранённое состояние плюс незаписанные правки.
    //
    // Это то представление, против которого окно считает доступность — и
    // именно поэтому строка «Debug Panel» гаснет в тот же миг, когда сняли
    // галочку «Sign-in Enabled», ещё до Apply. Меню в это же время считает
    // доступность против сохранённого состояния и ведёт себя как раньше.
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
            // Если за символом стоит зарегистрированная настройка, отвечаем по
            // отложенному значению. Незарегистрированные символы (их может
            // поставить кто-то руками в Player Settings) читаем из
            // сохранённого состояния — других сведений о них нет.
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

            // Значение, равное сохранённому, НЕ храним. Из-за этого «изменено
            // ли» — это просто наличие ключа, а два щелчка по галочке сами
            // убирают подсветку.
            if (value == _saved.GetValue(optionId))
                _pending.Remove(optionId);
            else
                _pending[optionId] = value;

            if (!(option is DefineOption define) || value == ProjectOption.On)
                return;

            // Каскад: выключили вход — отладке входа взяться уже не от чего.
            // Это НЕ то же, что погасить строку: символ действительно уходит
            // из набора, ровно как в XsollaIdMenu.SetDefine.
            foreach (string dependent in define.CascadeOffSymbols)
            {
                if (ProjectOptionRegistry.TryGetDefineOption(dependent, out DefineOption cascaded))
                    Set(cascaded.Id, ProjectOption.Off);
            }
        }

        public void Clear(string optionId) => _pending.Remove(optionId);

        public void ClearAll() => _pending.Clear();
    }
}
