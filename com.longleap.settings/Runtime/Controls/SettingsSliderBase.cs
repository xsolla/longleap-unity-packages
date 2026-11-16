using UnityEngine;

namespace LongLeap.Settings.Controls
{
    public abstract class SettingsSliderBase : MonoBehaviour
    {
        public abstract void Bind(ISettingsService service, NumericSetting<float> setting);
        public abstract void Bind(ISettingsService service, NumericSetting<int> setting);
    }
}
