using System.Threading;
using System.Threading.Tasks;

namespace LongLeap.Backpack
{
    // BackPack is switched off: there is no config or it is empty. Makes no
    // requests, answers Failed("not_configured") to everything, and the player is
    // always a guest.
    //
    // Why, if BackPackService without an endpoint behaves the same: so consumers
    // do not check the service for null, and "off" is visible from the type in the
    // container and in the log. Lives here rather than in com.longleap.nullproviders: that
    // package does not depend on features, and would otherwise pull backpack into
    // every game.
    //
    // With it the achievement wrapper saves everything locally (IsAvailable is
    // false), the backpack window shows an error, and the connection check
    // answers "not connected".
    public sealed class NullBackPackService : IBackPackService
    {
        public const string Reason = "not_configured";

        public static readonly NullBackPackService Instance = new NullBackPackService();

        private NullBackPackService()
        {
        }

        public bool IsAvailable => false;

        public bool HasXsollaId => false;

        public string XsollaId => null;

        public Task<BackPackReply> SendEventAsync(
            string eventName, string eventId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(BackPackReply.Failed(Reason));

        public Task<BackPackRewardsReply> GetRewardsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(BackPackRewardsReply.Failed(Reason));

        public Task<BackPackDeleteResult> DeleteItemAsync(
            string itemId, CancellationToken cancellationToken = default) =>
            Task.FromResult(BackPackDeleteResult.Failed(Reason));

        public Task<BackPackConnectionResult> CheckConnectionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(BackPackConnectionResult.Failed(Reason));

        public override string ToString() => "NullBackPackService";
    }
}
