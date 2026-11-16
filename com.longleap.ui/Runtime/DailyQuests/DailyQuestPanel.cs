using System;
using System.Collections.Generic;
using LongLeap.DailyQuests;
using UnityEngine;

namespace LongLeap.UI
{
    /// <summary>
    /// The "PLAYER STATS / DAILY QUESTS" panel in the main menu.
    ///
    /// Two parts, both vertical lists:
    /// <list type="bullet">
    /// <item>top: player stats — which ones is up to the game (<see cref="IPlayerStatsSource"/>:
    /// e.g. all-time score, today's score and money);</item>
    /// <item>bottom: active daily quests, with status and title
    /// (<see cref="DailyQuestItemView"/>). Rewards wait for an explicit click
    /// on the completed quest row or its ACTIVATE badge.</item>
    /// </list>
    ///
    /// Stat and quest rows are instantiated from prefabs in Awake / when the set changes.
    /// The quest service and stats source are passed in by the game menu, which gets them
    /// from the game's loading sequence: the UI package doesn't know about the service
    /// container. Without quests the bottom part is empty, without stats the top one;
    /// everything else still works.
    /// </summary>
    public sealed class DailyQuestPanel : MonoBehaviour
    {
        // =========================================================
        // STATS
        // =========================================================

        [Header("Stats")]
        [SerializeField] private PlayerStatRowView _statRowPrefab;

        [Tooltip("Parent for stat rows — an object with a VerticalLayoutGroup.")]
        [SerializeField] private RectTransform _statsRoot;

        // =========================================================
        // QUESTS
        // =========================================================

        [Header("Quests")]
        [SerializeField] private DailyQuestItemView _questItemPrefab;
        [SerializeField] private TMPro.TMP_FontAsset _rewardFlyoutFont;

        [Tooltip("Parent for quest rows — an object with a VerticalLayoutGroup.")]
        [SerializeField] private RectTransform _questsRoot;

        private readonly List<DailyQuestItemView> _questItems = new();

        private readonly List<(PlayerStat stat, PlayerStatRowView row)> _statRows = new();

        private IDailyQuestService _quests;
        private IPlayerStatsSource _stats;
        private IUiSounds _sounds;

        public void BindSounds(IUiSounds sounds) => _sounds = sounds;

        /// <summary>A reward claim is already in progress — repeated calls do nothing.</summary>
        private bool _claiming;

        // =========================================================
        // BINDING
        // =========================================================

        /// <summary>Give the panel the quest service; <c>null</c> unsubscribes.</summary>
        public void BindQuests(IDailyQuestService quests)
        {
            if (_quests == quests) return;

            if (_quests != null)
                _quests.Changed -= OnQuestsChanged;

            _quests = quests;

            if (_quests != null)
                _quests.Changed += OnQuestsChanged;

            ShowQuests();
        }

        /// <summary>Give the panel the player stats; <c>null</c> unsubscribes and removes the rows.</summary>
        public void BindStats(IPlayerStatsSource stats)
        {
            if (_stats == stats) return;

            if (_stats != null)
                _stats.Changed -= ShowStats;

            _stats = stats;

            if (_stats != null)
                _stats.Changed += ShowStats;

            RebuildStats();
        }

        /// <summary>
        /// Re-read the progress of quests completed during the run. Called by the menu
        /// when it opens: after a run, the score and quests have changed.
        /// </summary>
        public void Refresh()
        {
            _quests?.Tick();

            ShowStats();
            ShowQuests();
        }

        // =========================================================
        // UNITY
        // =========================================================

        private void OnEnable() => Refresh();

        /// <summary>While the panel is on screen, watch for the day rollover — a new set of quests.</summary>
        private void Update() => _quests?.Tick();

        private void OnDestroy()
        {
            BindQuests(null);
            BindStats(null);
        }

        // =========================================================
        // STATS
        // =========================================================

        /// <summary>Recreate the stat rows from the set provided by the source.</summary>
        private void RebuildStats()
        {
            foreach (var (_, row) in _statRows)
                if (row != null) Destroy(row.gameObject);

            _statRows.Clear();

            if (_stats?.Stats != null && _statRowPrefab != null && _statsRoot != null)
            {
                foreach (var stat in _stats.Stats)
                {
                    if (stat == null) continue;

                    var row = Instantiate(_statRowPrefab, _statsRoot);
                    row.SetLabel(stat.Label);
                    row.SetCurrency(stat.IsCurrency);
                    _statRows.Add((stat, row));
                }
            }

            ShowStats();
        }

        private void ShowStats()
        {
            foreach (var (stat, row) in _statRows)
                if (row != null) row.SetValue(stat.Value());
        }

        // =========================================================
        // QUESTS
        // =========================================================

        /// <summary>
        /// Quest progress also changes during a run, while the menu is closed;
        /// there's no point redrawing then: the panel re-reads everything in OnEnable.
        /// Completion updates the row; rewards still wait for a click.
        /// </summary>
        private void OnQuestsChanged()
        {
            if (!isActiveAndEnabled) return;

            ShowQuests();
        }

        private void ShowQuests()
        {
            var active = _quests?.GetActive() ?? Array.Empty<QuestStatus>();

            for (int i = 0; i < active.Length; i++)
            {
                var item = GetOrCreateItem(i);
                if (item == null) break;

                item.gameObject.SetActive(true);
                item.Set(active[i]);
            }

            for (int i = active.Length; i < _questItems.Count; i++)
                _questItems[i].gameObject.SetActive(false);
        }

        private DailyQuestItemView GetOrCreateItem(int index)
        {
            if (index < _questItems.Count)
                return _questItems[index];

            if (_questItemPrefab == null || _questsRoot == null) return null;

            var item = Instantiate(_questItemPrefab, _questsRoot);
            item.ClaimRequested += OnClaimRequested;
            _questItems.Add(item);
            return item;
        }

        /// <summary>Activate only the completed quest the player clicked.</summary>
        private void OnClaimRequested(QuestStatus status) => ClaimQuest(status);

        /// <summary>
        /// Claim one reward after a click. Failed saves retain the ACTIVATE badge;
        /// only a successful claim starts the reward flyout.
        /// </summary>
        private async void ClaimQuest(QuestStatus status)
        {
            if (_quests == null || _claiming || status == null || !status.CanClaim) return;

            _claiming = true;

            try
            {
                if (!await _quests.ClaimAsync(status.Id))
                    Debug.LogWarning($"[DailyQuests] failed to claim reward for '{status.Id.Value}'.", this);
                else
                    DailyQuestRewardFlyout.Play(transform, status.Definition, _rewardFlyoutFont, _sounds);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
            finally
            {
                _claiming = false;
            }
        }
    }
}
