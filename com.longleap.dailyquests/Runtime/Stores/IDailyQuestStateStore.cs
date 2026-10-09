using System.Threading.Tasks;

namespace LongLeap.DailyQuests
{
    public interface IDailyQuestStateStore
    {
        /// <summary>
        /// Сохранённое состояние. Сохранения ещё нет — пустое состояние
        /// (<see cref="DailyQuestsState.Empty"/>). Сохранение есть, но не
        /// читается — <see cref="DailyQuestsState.Unreadable"/>: сервис тогда
        /// не выдаёт набор до следующего сброса. Само хранилище недоступно —
        /// <c>null</c>: сервис выключается целиком. В обоих случаях начать с
        /// нуля нельзя — забранную награду можно было бы забрать второй раз.
        /// </summary>
        Task<DailyQuestsState> LoadAsync();

        Task SaveAsync(DailyQuestsState state);
    }
}
