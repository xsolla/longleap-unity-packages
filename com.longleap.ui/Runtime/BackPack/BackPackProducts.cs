using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Backpack;

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

    /// <summary>
    /// What one backpack item holds, split into categories. Implemented by the game: the server
    /// passes attributes through as they are, and which categories exist and what they mean
    /// is known only to the game. The window needs just one category — <see cref="Rewards"/>.
    /// </summary>
    public interface IBackPackItemCategories
    {
        /// <summary>
        /// Reward keys — product ids (<see cref="IBackPackProduct.Id"/>) activating the item grants.
        /// Never null; empty if the item grants nothing the game knows.
        /// </summary>
        IReadOnlyList<string> Rewards { get; }
    }

    /// <summary>
    /// Decides which backpack items the window shows and what each grants (its categories).
    /// Implemented by the game.
    /// </summary>
    public interface IBackPackItemCategorizer
    {
        /// <summary>
        /// The items that become cards, in display order, chosen from everything the backpack holds.
        /// A skipped item is neither shown nor activated and stays in the backpack on the server.
        /// Called on every backpack load; items are never null.
        /// </summary>
        IReadOnlyList<BackPackRewardItem> SelectShown(IReadOnlyList<BackPackRewardItem> items);

        /// <summary>Never null. Called once per item per backpack load — the window caches the result.</summary>
        IBackPackItemCategories Categorize(BackPackRewardItem item);
    }

    /// <summary>
    /// How an activated item is spent — so it can't be activated again. Implemented by the game when
    /// deleting on the server isn't always possible (e.g. items of other projects); without one the window
    /// deletes the item on the server (<see cref="IBackPackService.DeleteItemAsync"/>).
    /// </summary>
    public interface IBackPackItemSpender
    {
        /// <summary>
        /// Spend the item. Its products are granted only if the result is
        /// <see cref="BackPackDeleteResult.IsDeleted"/> — so never report spent what isn't.
        /// Called once per activated item, one at a time.
        /// </summary>
        Task<BackPackDeleteResult> SpendAsync(BackPackRewardItem item, IBackPackService backPack);
    }
}
