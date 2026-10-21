using System;
using System.Threading.Tasks;

namespace LongLeap.Core.Leagues
{
    /// <summary>
    /// Backend of <see cref="LeagueService"/>: one league server API for one
    /// player. Every method throws on a failed request; the service decides
    /// what a failure means.
    /// </summary>
    public interface ILeagueProvider : IDisposable
    {
        Task<LeagueState> GetStateAsync(string leagueId);

        /// <summary>Adds the score to the player's sum for the current period.</summary>
        Task<LeagueState> SubmitScoreAsync(string leagueId, long score, string displayName);

        Task<LeagueClaim> ClaimAsync(string leagueId, int periodNumber);

        /// <summary>
        /// Debug: fills the player's group with fake players. Null when the
        /// player has not submitted this period.
        /// </summary>
        Task<LeagueState> DebugFillGroupAsync(string leagueId);

        /// <summary>Debug: closes the current period now, for every group.</summary>
        Task DebugClosePeriodAsync(string leagueId);
    }
}
