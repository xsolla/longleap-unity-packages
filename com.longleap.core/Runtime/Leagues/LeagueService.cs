using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace LongLeap.Core.Leagues
{
    /// <summary>
    /// <see cref="ILeagueService"/> over an <see cref="ILeagueProvider"/>.
    ///
    /// The league id is the prefix plus the base id, so development builds
    /// can play in a separate copy of the league (as leaderboards do with
    /// their "dev_" boards).
    ///
    /// Failed submits are not lost: the score is kept and added to the next
    /// submit, so the player's sum still ends up right once the server is
    /// reachable again (within the same launch).
    /// </summary>
    public sealed class LeagueService : ILeagueService
    {
        private readonly ILeagueProvider _provider;
        private readonly Func<string> _displayNameResolver;
        private readonly HashSet<int> _claiming = new HashSet<int>();
        private readonly object _lock = new object();

        private long _pendingScore;
        private bool _disposed;

        public LeagueService(string prefix, string leagueId, ILeagueProvider provider,
            Func<string> displayNameResolver = null)
        {
            if (string.IsNullOrWhiteSpace(leagueId))
                throw new ArgumentException($"{nameof(LeagueService)}: leagueId is empty.", nameof(leagueId));

            LeagueId = (prefix ?? string.Empty) + leagueId;
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _displayNameResolver = displayNameResolver ?? (() => "Player");
        }

        public string LeagueId { get; }

        public LeagueState State { get; private set; }

        public event Action<LeagueState> StateChanged;

        public event Action<LeagueResult> RewardClaimed;

        public async Task<LeagueState> RefreshAsync()
        {
            if (_disposed)
                return State;

            try
            {
                LeagueState state = await _provider.GetStateAsync(LeagueId);
                Debug.Log($"[League] state: {state}");
                await ApplyAsync(state);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[League] refresh of '{LeagueId}' failed: {ex.Message}");
            }

            return State;
        }

        public async Task SubmitScoreAsync(long score)
        {
            if (_disposed || score < 0)
                return;

            long toSend;

            lock (_lock)
            {
                _pendingScore += score;
                toSend = _pendingScore;
                _pendingScore = 0;
            }

            if (toSend <= 0)
                return;

            try
            {
                LeagueState state = await _provider.SubmitScoreAsync(LeagueId, toSend, DisplayName());
                Debug.Log($"[League] submitted {toSend} — {state}");
                await ApplyAsync(state);
            }
            catch (Exception ex)
            {
                lock (_lock)
                    _pendingScore += toSend;

                Debug.LogWarning($"[League] submit of {toSend} to '{LeagueId}' failed, kept for the next submit: {ex.Message}");
            }
        }

        public async Task<bool> DebugFillGroupAsync()
        {
            if (_disposed)
                return false;

            try
            {
                LeagueState state = await _provider.DebugFillGroupAsync(LeagueId);

                if (state == null)
                {
                    Debug.LogWarning("[League] debug fill: the player has no group this period — submit a run first.");
                    return false;
                }

                Debug.Log($"[League] debug fill done — {state}");
                await ApplyAsync(state);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[League] debug fill of '{LeagueId}' failed: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DebugClosePeriodAsync()
        {
            if (_disposed)
                return false;

            try
            {
                await _provider.DebugClosePeriodAsync(LeagueId);
                Debug.Log($"[League] debug close of '{LeagueId}' done.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[League] debug close of '{LeagueId}' failed: {ex.Message}");
                return false;
            }

            await RefreshAsync();
            return true;
        }

        public void Dispose()
        {
            _disposed = true;
            _provider.Dispose();
        }

        private async Task ApplyAsync(LeagueState state)
        {
            if (_disposed || state == null)
                return;

            State = state;
            StateChanged?.Invoke(state);

            await ClaimRewardsAsync(state);
        }

        /// <summary>
        /// Claims every unclaimed result of the state. The server flips each
        /// result once, so a reward is credited once even if two devices or
        /// two overlapping refreshes try at the same time.
        /// </summary>
        private async Task ClaimRewardsAsync(LeagueState state)
        {
            foreach (LeagueResult result in state.Unclaimed)
            {
                if (result.Claimed || result.Reward <= 0)
                    continue;

                lock (_lock)
                {
                    if (!_claiming.Add(result.PeriodNumber))
                        continue;
                }

                try
                {
                    LeagueClaim claim = await _provider.ClaimAsync(LeagueId, result.PeriodNumber);

                    if (!claim.Claimed)
                    {
                        Debug.Log($"[League] reward of period {result.PeriodNumber} was already claimed.");
                        continue;
                    }

                    Debug.Log($"[League] claimed reward {claim.Reward} of period {result.PeriodNumber}.");

                    try
                    {
                        RewardClaimed?.Invoke(result);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[League] crediting the reward of period {result.PeriodNumber} failed: {ex}");
                    }
                }
                catch (Exception ex)
                {
                    // Stays unclaimed on the server: the next refresh retries.
                    Debug.LogWarning($"[League] claim of period {result.PeriodNumber} failed: {ex.Message}");
                }
                finally
                {
                    lock (_lock)
                        _claiming.Remove(result.PeriodNumber);
                }
            }
        }

        private string DisplayName()
        {
            string name = _displayNameResolver();
            return string.IsNullOrWhiteSpace(name) ? "Player" : name;
        }
    }
}
