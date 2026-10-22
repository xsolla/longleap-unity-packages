using System;
using System.Collections.Generic;
using LongLeap.DailyQuests;
using UnityEngine;

namespace LongLeap.UI
{
    /// <summary>
    /// Панель «PLAYER STATS / DAILY QUESTS» в главном меню.
    ///
    /// Две части, обе — вертикальные списки:
    /// <list type="bullet">
    /// <item>сверху статы игрока — какие именно, решает игра (<see cref="IPlayerStatsSource"/>:
    /// например, очки за всё время, за сегодня и деньги);</item>
    /// <item>снизу активные ежедневные квесты: статус и название
    /// (<see cref="DailyQuestItemView"/>). Rewards wait for an explicit click
    /// on the completed quest row or its ACTIVATE badge.</item>
    /// </list>
    ///
    /// Строки статов и квестов создаются из префабов в Awake / при смене набора.
    /// Сервис квестов и источник статов приносит меню игры, а ему — загрузка: пакет UI
    /// не знает про контейнер сервисов. Без квестов нижняя часть пуста, без статов —
    /// верхняя; остальное работает.
    /// </summary>
    public sealed class DailyQuestPanel : MonoBehaviour
    {
        // =========================================================
        // СТАТЫ
        // =========================================================

        [Header("Stats")]
        [SerializeField] private PlayerStatRowView _statRowPrefab;

        [Tooltip("Куда класть строки статов — объект с VerticalLayoutGroup.")]
        [SerializeField] private RectTransform _statsRoot;

        // =========================================================
        // КВЕСТЫ
        // =========================================================

        [Header("Quests")]
        [SerializeField] private DailyQuestItemView _questItemPrefab;
        [SerializeField] private TMPro.TMP_FontAsset _rewardFlyoutFont;

        [Tooltip("Куда класть строки квестов — объект с VerticalLayoutGroup.")]
        [SerializeField] private RectTransform _questsRoot;

        private readonly List<DailyQuestItemView> _questItems = new();

        private readonly List<(PlayerStat stat, PlayerStatRowView row)> _statRows = new();

        private IDailyQuestService _quests;
        private IPlayerStatsSource _stats;
        private IUiSounds _sounds;

        public void BindSounds(IUiSounds sounds) => _sounds = sounds;

        /// <summary>Награды уже забираются — повторный вызов ничего не делает.</summary>
        private bool _claiming;

        // =========================================================
        // ПРИВЯЗКА
        // =========================================================

        /// <summary>Отдать панели сервис квестов; <c>null</c> — отписаться.</summary>
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

        /// <summary>Отдать панели статы игрока; <c>null</c> — отписаться и убрать строки.</summary>
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
        /// Перечитать прогресс выполненных в заезде квестов. Зовёт меню
        /// при открытии: после заезда очки и квесты другие.
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

        /// <summary>Пока панель на экране, следим за сменой суток — новый набор квестов.</summary>
        private void Update() => _quests?.Tick();

        private void OnDestroy()
        {
            BindQuests(null);
            BindStats(null);
        }

        // =========================================================
        // СТАТЫ
        // =========================================================

        /// <summary>Строки статов заново — по набору, который отдал источник.</summary>
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
        // КВЕСТЫ
        // =========================================================

        /// <summary>
        /// Прогресс квестов меняется и во время заезда, когда меню закрыто, —
        /// тогда перерисовывать незачем: панель перечитает всё в OnEnable.
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
                    Debug.LogWarning($"[DailyQuests] награда за '{status.Id.Value}' не забрана.", this);
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
