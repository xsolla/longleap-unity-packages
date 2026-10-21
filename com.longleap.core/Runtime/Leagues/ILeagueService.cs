using System;
using System.Threading.Tasks;

namespace LongLeap.Core.Leagues
{
    /// <summary>
    /// A weekly league for the current player: submit scores, read the
    /// state, and get the rewards of closed periods.
    ///
    /// Rewards are claimed automatically whenever a state arrives with
    /// unclaimed results; <see cref="RewardClaimed"/> fires once per result,
    /// and its handler is where the game credits the reward.
    /// </summary>
    public interface ILeagueService : IDisposable
    {
        /// <summary>The league id as sent to the server (prefix included).</summary>
        string LeagueId { get; }

        /// <summary>The last state received; null until the first one arrives.</summary>
        LeagueState State { get; }

        /// <summary>A new state arrived.</summary>
        event Action<LeagueState> StateChanged;

        /// <summary>
        /// A result's reward was claimed on the server. Fires exactly once per
        /// result across all devices: credit the reward here.
        /// </summary>
        event Action<LeagueResult> RewardClaimed;

        /// <summary>
        /// Reads the state from the server. Returns the last known state (may
        /// be null) when the request fails.
        /// </summary>
        Task<LeagueState> RefreshAsync();

        /// <summary>
        /// Adds the score to the player's sum for the period. A score that
        /// failed to send is kept and sent with the next submit.
        /// </summary>
        Task SubmitScoreAsync(long score);

        /// <summary>Debug: fill the player's group with fake players.</summary>
        Task<bool> DebugFillGroupAsync();

        /// <summary>Debug: close the current period for every group now.</summary>
        Task<bool> DebugClosePeriodAsync();
    }
}
