namespace LongLeap.XsollaId
{
    // Хранилище сессии между запусками.
    //
    // Отдельный шов, а не прямая работа с PlayerPrefs, чтобы тесты гоняли
    // машину состояний на InMemoryPlayerPrefs и не зависели от порядка
    // выполнения (ключи PlayerPrefs глобальны и переживают тест).
    public interface IXsollaIdSessionStore
    {
        // null — сохранённой сессии нет либо она нечитаемая.
        XsollaIdSession Load();

        void Save(XsollaIdSession session);

        void Clear();
    }
}
