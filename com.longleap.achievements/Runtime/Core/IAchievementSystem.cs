using System.Threading.Tasks;
using LongLeap.Stats;

namespace LongLeap.Achievements
{
    public interface IAchievementSystem
    {
        Task Initialize(IAchievementProvider provider, IAchievementStateStore stateStore, IAchievementGameBridge gameBridge = null, IStatSystem stats = null);

        void Shutdown();

        void ForceUnlock(AchievementId achievementId);

        void ResetAll();

        AchievementStatus[] GetAll();
        AchievementStatus[] GetByGroup(GroupId groupId);
        AchievementStatus[] GetAvailable();
        AchievementStatus[] GetAchieved();
        AchievementStatus[] GetPartiallyAchieved();
        AchievementStatus GetStatus(AchievementId achievementId);
        GroupId[] GetGroups();
    }
}
