using UnityEngine;

namespace LongLeap.Settings.Controls
{
    public abstract class SettingsDropdownBase : MonoBehaviour
    {
        public abstract void Bind(ISettingsService service, Setting setting);
    }
}
