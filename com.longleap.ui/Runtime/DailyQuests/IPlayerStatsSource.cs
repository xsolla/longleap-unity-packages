using System;
using System.Collections.Generic;

namespace LongLeap.UI
{
    /// <summary>One row of the player stats list: caption and the current value as text.</summary>
    public sealed class PlayerStat
    {
        public PlayerStat(string label, Func<string> value, bool isCurrency = false)
        {
            Label = label;
            Value = value ?? throw new ArgumentNullException(nameof(value));
            IsCurrency = isCurrency;
        }

        public string Label { get; }

        /// <summary>Read on every refresh, so it always shows the live number.</summary>
        public Func<string> Value { get; }

        /// <summary>The row shows the currency icon.</summary>
        public bool IsCurrency { get; }
    }

    /// <summary>
    /// What the stats part of <see cref="DailyQuestPanel"/> shows. Which stats and how they are
    /// formatted is the game's business: it implements this over its own stats and wallet.
    /// </summary>
    public interface IPlayerStatsSource
    {
        IReadOnlyList<PlayerStat> Stats { get; }

        /// <summary>Some value changed outside a refresh (e.g. the wallet balance).</summary>
        event Action Changed;
    }
}
