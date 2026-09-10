using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LongLeap.Settings
{
    public interface ISettingsService
    {
        void Register(ISettingsGroup group);
        IReadOnlyList<ISettingsGroup> Groups { get; }
        void SetValue<T>(Setting<T> setting, T value);
        Task LoadAllAsync();
        Task SaveAllAsync();
        event Action<Setting> OnChanged;
    }
}
