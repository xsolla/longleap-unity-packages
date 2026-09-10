using System.Collections.Generic;

namespace LongLeap.Achievements
{
    public sealed class UnlockPropagator
    {
        private readonly Queue<AchievementId> _deferredQueue = new();

        public void Enqueue(AchievementId achievementId)
        {
            _deferredQueue.Enqueue(achievementId);
        }

        public bool HasPending => _deferredQueue.Count > 0;

        public List<AchievementId> GetAllPending()
        {
            return new List<AchievementId>(_deferredQueue);
        }

        public void Clear()
        {
            _deferredQueue.Clear();
        }
    }
}
