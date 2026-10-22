using System;
using System.Collections.Generic;

namespace LongLeap.UI
{
    /// <summary>When a backpack item may be handed out.</summary>
    [Flags]
    public enum ProductUseContext
    {
        None = 0,

        /// <summary>Right after a run starts (backpack opened before the run).</summary>
        RunStart = 1 << 0,

        /// <summary>In the middle of a run.</summary>
        InRun = 1 << 1,
    }

    /// <summary>
    /// What a backpack reward key gives the player. The server only attaches reward keys
    /// to an item; the game decides what each key means.
    /// </summary>
    public interface IBackPackProduct
    {
        string Id { get; }

        string Title { get; }

        /// <summary>How many of this product the player may hold at once.</summary>
        int Max { get; }

        /// <summary>Lines for the item card: icon and caption per effect.</summary>
        IReadOnlyList<BackPackRewardEntry> Rewards { get; }

        bool IsUsableIn(ProductUseContext context);

        bool CanApply();

        /// <summary>How many more the player accepts right now.</summary>
        int RemainingUses();

        void Apply();
    }

    public interface IBackPackProductCatalog
    {
        bool TryGetProduct(string id, out IBackPackProduct product);
    }
}
