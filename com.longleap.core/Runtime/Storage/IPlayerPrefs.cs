namespace LongLeap.Core
{
    public interface IPlayerPrefs
    {
        string GetString(string key, string defaultValue = "");
        int GetInt(string key, int defaultValue = 0);
        float GetFloat(string key, float defaultValue = 0f);
        void SetString(string key, string value);
        void SetInt(string key, int value);
        void SetFloat(string key, float value);
        bool HasKey(string key);
        void DeleteKey(string key);
        void Save();
    }
}
