using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LongLeap.Core.Leagues;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LongLeap.Core.Tests
{
    [TestFixture]
    public class LeagueServiceTests
    {
        // Scripted backend: records calls, fails submits on demand and claims
        // each period once, like the server does.
        private sealed class FakeProvider : ILeagueProvider
        {
            public readonly List<(string LeagueId, long Score)> Submits = new List<(string, long)>();
            public readonly List<int> Claims = new List<int>();
            public readonly HashSet<int> ClaimedOnServer = new HashSet<int>();

            public bool FailSubmits;
            public LeagueState NextState;

            public Task<LeagueState> GetStateAsync(string leagueId) => Task.FromResult(NextState);

            public Task<LeagueState> SubmitScoreAsync(string leagueId, long score, string displayName)
            {
                if (FailSubmits)
                    throw new InvalidOperationException("offline");

                Submits.Add((leagueId, score));
                return Task.FromResult(NextState);
            }

            public Task<LeagueClaim> ClaimAsync(string leagueId, int periodNumber)
            {
                Claims.Add(periodNumber);
                return Task.FromResult(new LeagueClaim(ClaimedOnServer.Add(periodNumber), 80));
            }

            public Task<LeagueState> DebugFillGroupAsync(string leagueId) => Task.FromResult(NextState);

            public Task DebugClosePeriodAsync(string leagueId) => Task.CompletedTask;

            public void Dispose()
            {
            }
        }

        private static LeagueState State(params LeagueResult[] unclaimed) =>
            new LeagueState(
                "dev_weekly",
                new[] { "backyard", "sidewalk" },
                new LeaguePeriod(2, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1)),
                1,
                null,
                unclaimed.Length > 0 ? unclaimed[0] : null,
                unclaimed);

        private static LeagueResult Result(int period, int reward) =>
            new LeagueResult(period, 3, 1000, 0, 1, LeagueOutcome.Promoted, reward, false);

        [Test]
        public void LeagueId_IncludesThePrefix()
        {
            var service = new LeagueService("dev_", "weekly", new FakeProvider());

            Assert.AreEqual("dev_weekly", service.LeagueId);
        }

        [Test]
        public async Task Submit_SendsTheScoreAndStoresTheState()
        {
            var provider = new FakeProvider { NextState = State() };
            var service = new LeagueService("dev_", "weekly", provider);
            LeagueState received = null;
            service.StateChanged += state => received = state;

            await service.SubmitScoreAsync(500);

            Assert.AreEqual(1, provider.Submits.Count);
            Assert.AreEqual("dev_weekly", provider.Submits[0].LeagueId);
            Assert.AreEqual(500, provider.Submits[0].Score);
            Assert.AreSame(provider.NextState, service.State);
            Assert.AreSame(provider.NextState, received);
        }

        [Test]
        public async Task FailedSubmit_IsAddedToTheNextOne()
        {
            var provider = new FakeProvider { NextState = State(), FailSubmits = true };
            var service = new LeagueService("", "weekly", provider);

            LogAssert.Expect(LogType.Warning, new Regex("submit of 300"));
            await service.SubmitScoreAsync(300);

            provider.FailSubmits = false;
            await service.SubmitScoreAsync(200);

            Assert.AreEqual(1, provider.Submits.Count);
            Assert.AreEqual(500, provider.Submits[0].Score);
        }

        [Test]
        public async Task UnclaimedReward_IsClaimedAndReportedOnce()
        {
            var provider = new FakeProvider { NextState = State(Result(1, 80)) };
            var service = new LeagueService("", "weekly", provider);
            var credited = new List<LeagueResult>();
            service.RewardClaimed += credited.Add;

            await service.RefreshAsync();
            // A stale reply still lists it as unclaimed: the second claim
            // comes back "already claimed" and is not credited again.
            await service.RefreshAsync();

            Assert.AreEqual(new[] { 1, 1 }, provider.Claims.ToArray());
            Assert.AreEqual(1, credited.Count);
            Assert.AreEqual(1, credited[0].PeriodNumber);
        }

        [Test]
        public async Task ZeroReward_IsNotClaimed()
        {
            var provider = new FakeProvider { NextState = State(Result(1, 0)) };
            var service = new LeagueService("", "weekly", provider);

            await service.RefreshAsync();

            Assert.IsEmpty(provider.Claims);
        }

        [Test]
        public void Group_ZoneOf_UsesTheBoundaries()
        {
            var group = new LeagueGroup(1, 5, 16, 7, 100, 50, Array.Empty<LeagueEntry>());

            Assert.AreEqual(LeagueZone.Promotion, group.ZoneOf(5));
            Assert.AreEqual(LeagueZone.Safe, group.ZoneOf(6));
            Assert.AreEqual(LeagueZone.Safe, group.ZoneOf(15));
            Assert.AreEqual(LeagueZone.Demotion, group.ZoneOf(16));
        }

        [Test]
        public void Group_ZoneOf_EdgeTiersHaveNoZone()
        {
            var top = new LeagueGroup(1, 0, 16, 1, 100, 0, Array.Empty<LeagueEntry>());
            var bottom = new LeagueGroup(1, 5, 0, 20, 100, 0, Array.Empty<LeagueEntry>());

            Assert.AreEqual(LeagueZone.Safe, top.ZoneOf(1));
            Assert.AreEqual(LeagueZone.Safe, bottom.ZoneOf(20));
        }
    }
}
