using System;
using System.Collections.Generic;

namespace LongLeap.XsollaId.Leagues
{
    /// <summary>
    /// The player's view of a league: their tier, the current period, their
    /// group in it (once they have submitted a score this period) and the
    /// results of closed periods.
    /// </summary>
    public sealed class LeagueState
    {
        public LeagueState(
            string leagueId,
            IReadOnlyList<string> tiers,
            LeaguePeriod period,
            int tier,
            LeagueGroup group,
            LeagueResult lastResult,
            IReadOnlyList<LeagueResult> unclaimed)
        {
            LeagueId = leagueId;
            Tiers = tiers ?? Array.Empty<string>();
            Period = period;
            Tier = tier;
            Group = group;
            LastResult = lastResult;
            Unclaimed = unclaimed ?? Array.Empty<LeagueResult>();
        }

        public string LeagueId { get; }

        /// <summary>Tier ids from the lowest to the highest.</summary>
        public IReadOnlyList<string> Tiers { get; }

        public LeaguePeriod Period { get; }

        /// <summary>The player's current tier: index into <see cref="Tiers"/>.</summary>
        public int Tier { get; }

        /// <summary>The player's group this period; null until they submit a score.</summary>
        public LeagueGroup Group { get; }

        /// <summary>
        /// How the period right before the current one ended for the player;
        /// null if they did not take part in it.
        /// </summary>
        public LeagueResult LastResult { get; }

        /// <summary>Results with a reward not claimed yet.</summary>
        public IReadOnlyList<LeagueResult> Unclaimed { get; }

        /// <summary>The player has submitted a score this period.</summary>
        public bool IsJoined => Group != null;

        public string TierId => TierIdAt(Tier);

        public string TierIdAt(int tier) =>
            tier >= 0 && tier < Tiers.Count ? Tiers[tier] : string.Empty;

        public override string ToString() =>
            $"{LeagueId} period {Period.Number}, tier '{TierId}', " +
            (IsJoined ? $"place {Group.Place}/{Group.Entries.Count}, score {Group.Score}" : "not joined") +
            (LastResult != null ? $", last result: {LastResult}" : string.Empty) +
            $", unclaimed {Unclaimed.Count}";
    }

    public readonly struct LeaguePeriod
    {
        public LeaguePeriod(int number, DateTimeOffset startsAt, DateTimeOffset endsAt)
        {
            Number = number;
            StartsAt = startsAt;
            EndsAt = endsAt;
        }

        public int Number { get; }

        public DateTimeOffset StartsAt { get; }

        public DateTimeOffset EndsAt { get; }

        /// <summary>Time until the period ends; never negative.</summary>
        public TimeSpan TimeLeft(DateTimeOffset now)
        {
            TimeSpan left = EndsAt - now;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    public enum LeagueZone
    {
        Promotion,
        Safe,
        Demotion
    }

    public sealed class LeagueGroup
    {
        public LeagueGroup(
            int number,
            int promotePlaces,
            int demoteFromPlace,
            int place,
            long score,
            long scoreToPromotion,
            IReadOnlyList<LeagueEntry> entries)
        {
            Number = number;
            PromotePlaces = promotePlaces;
            DemoteFromPlace = demoteFromPlace;
            Place = place;
            Score = score;
            ScoreToPromotion = scoreToPromotion;
            Entries = entries ?? Array.Empty<LeagueEntry>();
        }

        public int Number { get; }

        /// <summary>Places 1..PromotePlaces move up; 0 in the top tier.</summary>
        public int PromotePlaces { get; }

        /// <summary>This place and below move down; 0 in the bottom tier.</summary>
        public int DemoteFromPlace { get; }

        /// <summary>The player's own place.</summary>
        public int Place { get; }

        /// <summary>The player's sum for the period.</summary>
        public long Score { get; }

        /// <summary>
        /// Score the player still needs to reach the promotion zone; 0 when
        /// already there or there is no promotion zone.
        /// </summary>
        public long ScoreToPromotion { get; }

        /// <summary>The whole group, best first.</summary>
        public IReadOnlyList<LeagueEntry> Entries { get; }

        public LeagueZone ZoneOf(int place)
        {
            if (PromotePlaces > 0 && place <= PromotePlaces)
                return LeagueZone.Promotion;

            if (DemoteFromPlace > 0 && place >= DemoteFromPlace)
                return LeagueZone.Demotion;

            return LeagueZone.Safe;
        }
    }

    public readonly struct LeagueEntry
    {
        public LeagueEntry(int place, string displayName, long score, bool isMe, int reward = 0)
        {
            Place = place;
            DisplayName = displayName;
            Score = score;
            IsMe = isMe;
            Reward = reward;
        }

        public int Place { get; }

        public string DisplayName { get; }

        public long Score { get; }

        public bool IsMe { get; }

        /// <summary>What this place earns if the period ended now; 0 for none.</summary>
        public int Reward { get; }
    }

    public enum LeagueOutcome
    {
        Promoted,
        Stayed,
        Demoted
    }

    /// <summary>How a closed period ended for the player.</summary>
    public sealed class LeagueResult
    {
        public LeagueResult(
            int periodNumber,
            int place,
            long score,
            int tier,
            int newTier,
            LeagueOutcome outcome,
            int reward,
            bool claimed)
        {
            PeriodNumber = periodNumber;
            Place = place;
            Score = score;
            Tier = tier;
            NewTier = newTier;
            Outcome = outcome;
            Reward = reward;
            Claimed = claimed;
        }

        public int PeriodNumber { get; }

        public int Place { get; }

        public long Score { get; }

        /// <summary>The tier the player played the period in.</summary>
        public int Tier { get; }

        /// <summary>The tier the player moved to.</summary>
        public int NewTier { get; }

        public LeagueOutcome Outcome { get; }

        public int Reward { get; }

        public bool Claimed { get; }

        public override string ToString() =>
            $"period {PeriodNumber}: place {Place}, {Outcome} {Tier}→{NewTier}, reward {Reward}" +
            (Claimed ? " (claimed)" : string.Empty);
    }

    /// <summary>
    /// Reply to a claim. <see cref="Claimed"/> is true only for the call that
    /// actually claimed the reward: credit it then, and never again.
    /// </summary>
    public readonly struct LeagueClaim
    {
        public LeagueClaim(bool claimed, int reward)
        {
            Claimed = claimed;
            Reward = reward;
        }

        public bool Claimed { get; }

        public int Reward { get; }
    }
}
