using System.Threading.Tasks;

namespace LongLeap.DailyQuests
{
    public interface IDailyQuestStateStore
    {
        /// <summary>
        /// Сохранённое состояние. Сохранения ещё нет — пустое состояние
        /// (<see cref="DailyQuestsState.Empty"/>). Само хранилище недоступно —
        /// <c>null</c>: сервис тогда выключается целиком, а не начинает с нуля,
        /// иначе забранную награду можно было бы забрать второй раз.
        /// </summary>
        Task<DailyQuestsState> LoadAsync();

        Task SaveAsync(DailyQuestsState state);
    }
}
