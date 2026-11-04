using System;
using System.Threading.Tasks;
using LongLeap.Core.Leagues;

namespace LongLeap.Null
{
    /// <summary>
    /// League backend that keeps nothing: every read is a period-less state
    /// in the lowest tier, submits are dropped, there is never a reward.
    /// </summary>
    public sealed class NullLeagueProvider : ILeagueProvider
    {
        public Task<LeagueState> GetStateAsync(string leagueId) =>
            Task.FromResult(Empty(leagueId));

        public Task<LeagueState> SubmitScoreAsync(string leagueId, long score, string displayName) =>
            Task.FromResult(Empty(leagueId));

        public Task<LeagueClaim> ClaimAsync(string leagueId, int periodNumber) =>
            Task.FromResult(new LeagueClaim(false, 0));

        public Task<LeagueState> DebugFillGroupAsync(string leagueId) =>
            Task.FromResult<LeagueState>(null);

        public Task DebugClosePeriodAsync(string leagueId) => Task.CompletedTask;

        public void Dispose()
        {
            // Nothing to release.
        }

        private static LeagueState Empty(string leagueId) =>
            new LeagueState(
                leagueId,
                Array.Empty<string>(),
                new LeaguePeriod(0, DateTimeOffset.MinValue, DateTimeOffset.MinValue),
                0,
                null,
                null,
                Array.Empty<LeagueResult>());
    }
}
