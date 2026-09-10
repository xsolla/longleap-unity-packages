using System.Threading.Tasks;

namespace LongLeap.Achievements
{
    public interface IAchievementStateStore
    {
        Task<AchievementStatesSnapshot> LoadAsync(AchievementDefinition[] definitions);
        Task MarkUnlockedAsync(AchievementId id, long timestamp);
        Task DeleteAllAsync();
    }
}
