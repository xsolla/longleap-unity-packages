using UnityEngine;

namespace LongLeap.Settings.Controls
{
    public abstract class SettingsToggleBase : MonoBehaviour
    {
        public abstract void Bind(ISettingsService service, Setting<bool> setting);
    }
}
