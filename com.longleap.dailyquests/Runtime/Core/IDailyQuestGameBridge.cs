namespace LongLeap.DailyQuests
{
    /// <summary>
    /// Точка, где квесты встречаются с игрой. Пакет сам ничего не начисляет —
    /// награду кладёт в кошелёк игра, в <see cref="ApplyRewards"/>.
    /// </summary>
    public interface IDailyQuestGameBridge
    {
        /// <summary>Выдан новый набор квестов (наступил новый период).</summary>
        void OnQuestsRotated(QuestStatus[] active);

        /// <summary>Квест выполнен — награду можно забирать. Сама она ещё не выдана.</summary>
        void OnQuestCompleted(QuestStatus status);

        /// <summary>Игрок забрал награду — начислить её.</summary>
        void ApplyRewards(QuestClaimResult result);
    }
}
