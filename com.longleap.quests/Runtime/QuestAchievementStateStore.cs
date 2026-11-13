using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Achievements;
using UnityEngine;

namespace LongLeap.Quests
{
    // The achievement store for achievements owned by Quests — the Quests
    // counterpart of BackPackAchievementStateStore, written to the same
    // IAchievementStateStore contract so a router above can pick either one.
    //
    // The rules differ from BackPack's on purpose:
    // - only a signed-in player counts: a guest's (or, with the service off, anyone's)
    //   quest achievement is SKIPPED — not saved, not counted;
    // - a signed-in player's unlock is saved at once, without waiting for the
    //   server; the event goes in the background and its answer is only logged.
    //
    // Note: AchievementSystem marks an achievement unlocked in memory before it
    // calls the store, so a skipped one still looks unlocked until the session ends.
    // On the next launch it is locked again and unlocks from stats once more.
    public sealed class QuestAchievementStateStore : IAchievementStateStore
    {
        private readonly IAchievementStateStore _inner;
        private readonly IQuestService _quests;
        private readonly Func<AchievementId, IReadOnlyDictionary<string, string>> _properties;

        // properties — what goes into the event's "properties" for an achievement; may be null.
        public QuestAchievementStateStore(
            IAchievementStateStore inner,
            IQuestService quests,
            Func<AchievementId, IReadOnlyDictionary<string, string>> properties = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _quests = quests;
            _properties = properties;
        }

        public Task<AchievementStatesSnapshot> LoadAsync(AchievementDefinition[] definitions) =>
            _inner.LoadAsync(definitions);

        public async Task MarkUnlockedAsync(AchievementId id, long timestamp)
        {
            if (_quests == null || !_quests.IsAvailable || !_quests.HasXsollaId)
            {
                Debug.Log($"[Quests] '{id.Value}' skipped — no signed-in player or the service is off; not counted.");
                return;
            }

            await _inner.MarkUnlockedAsync(id, timestamp);

            _ = SendInBackgroundAsync(id.Value, _properties?.Invoke(id));
        }

        public Task DeleteAllAsync() => _inner.DeleteAllAsync();

        private async Task SendInBackgroundAsync(string eventName, IReadOnlyDictionary<string, string> properties)
        {
            try
            {
                QuestReply reply = await _quests.SendEventAsync(eventName, properties);

                if (!reply.Success)
                    Debug.LogWarning($"[Quests] event '{eventName}' was not accepted ({reply.Error}).");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Quests] sending event '{eventName}' failed: {e.Message}");
            }
        }

        // Default properties: each stat objective as "statPath": "target".
        public static Func<AchievementId, IReadOnlyDictionary<string, string>> PropertiesFromObjectives(
            IEnumerable<AchievementDefinition> definitions)
        {
            var map = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);

            if (definitions != null)
            {
                foreach (var def in definitions)
                {
                    if (def?.Objectives == null)
                        continue;

                    var props = new Dictionary<string, string>();

                    foreach (var objective in def.Objectives)
                    {
                        if (objective != null && !string.IsNullOrEmpty(objective.StatPath))
                            props[objective.StatPath] = objective.StatTarget.ToString();
                    }

                    map[def.Id.Value] = props;
                }
            }

            return id => map.TryGetValue(id.Value, out var props) ? props : null;
        }
    }
}
