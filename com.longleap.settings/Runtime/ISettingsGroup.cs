namespace LongLeap.Settings
{
    public interface ISettingsGroup
    {
        string StorageKey { get; }
        string DisplayNameKey { get; }
        Setting[] All { get; }
        string Serialize();
        void Deserialize(string json);
    }
}
