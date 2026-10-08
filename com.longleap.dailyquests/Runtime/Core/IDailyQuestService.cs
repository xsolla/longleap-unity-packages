using System;
using System.Threading.Tasks;

namespace LongLeap.DailyQuests
{
    public interface IDailyQuestService
    {
        /// <summary>
        /// Что-то поменялось: прогресс, выполнение, забор награды, новый набор.
        /// UI в ответ просто перечитывает <see cref="GetActive"/>.
        /// </summary>
        event Action Changed;

        Task InitializeAsync();

        void Shutdown();

        /// <summary>Квесты текущего периода в порядке выдачи.</summary>
        QuestStatus[] GetActive();

        /// <summary>Статус активного квеста или <c>null</c>, если такого сейчас нет.</summary>
        QuestStatus GetStatus(QuestId questId);

        /// <summary>Сколько наград ждёт, пока их заберут, — для бейджа на кнопке.</summary>
        int ClaimableCount { get; }

        /// <summary>Сколько осталось до смены набора.</summary>
        TimeSpan TimeUntilReset { get; }

        /// <summary>
        /// Забрать награду выполненного квеста. <c>false</c> — квест не активен,
        /// не выполнен или уже забран; награда тогда не выдаётся.
        /// </summary>
        bool Claim(QuestId questId);

        /// <summary>Отладка: засчитать квест выполненным, не трогая статы.</summary>
        void ForceComplete(QuestId questId);

        /// <summary>Отладка: выдать новый набор прямо сейчас, как будто период сменился.</summary>
        void DebugSkipToNextResetWindow();

        /// <summary>Стереть состояние и выдать набор заново.</summary>
        void Reset();
    }
}
