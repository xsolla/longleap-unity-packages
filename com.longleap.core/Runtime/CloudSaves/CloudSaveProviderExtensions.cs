using System.Threading.Tasks;

namespace LongLeap.Core.CloudSaves
{
    public static class CloudSaveProviderExtensions
    {
        public static async Task SetIntAsync(this ICloudSaveProvider provider, CloudSaveKey key, int value)
        {
            await provider.SetStringAsync(key, value.ToString());
        }

        public static async Task<int> GetIntAsync(this ICloudSaveProvider provider, CloudSaveKey key, int defaultValue = 0)
        {
            string val = await provider.GetStringAsync(key);
            if (val == null) return defaultValue;
            return int.TryParse(val, out int result) ? result : defaultValue;
        }

        public static async Task SetFloatAsync(this ICloudSaveProvider provider, CloudSaveKey key, float value)
        {
            await provider.SetStringAsync(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        public static async Task<float> GetFloatAsync(this ICloudSaveProvider provider, CloudSaveKey key, float defaultValue = 0f)
        {
            string val = await provider.GetStringAsync(key);
            if (val == null) return defaultValue;
            return float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float result) ? result : defaultValue;
        }
    }
}
