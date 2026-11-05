using System.Collections.Generic;
using System.Threading.Tasks;

namespace LongLeap.Core.CloudSaves
{
    // Where the provider reads and writes.
    //
    // A save can have two owners: the guest (keyed by the anonymous uid) and an
    // account (keyed by an external account id). The scope is state of the
    // PROVIDER, not of the service: CloudSaveService knows nothing about storage
    // paths and works with keys only.
    //
    // A separate interface rather than extra methods on ICloudSaveProvider:
    // switching scope is not part of regular save access. Exactly one caller
    // does it (the game's save switch, on sign-in and sign-out); code that reads
    // and writes keys never needs to know about it.
    public interface IAccountCloudSaveScope
    {
        // True when the account's save is in use rather than the guest one.
        bool IsAccountScoped { get; }

        // Id of the account whose save is in use. Empty in the guest scope.
        string AccountId { get; }

        // Switch the scope. The choice must survive a scene reload and a
        // restart — otherwise after switching saves the game would come back to
        // the guest progress and the player would think the account save was lost.
        void UseAccount(string accountId);
        void UseGuest();

        // Read an account's save WITHOUT switching scope or applying anything.
        //
        // Used to build the summary for a "which save do you want?" window: the
        // contents must be shown BEFORE the player decides whether to take it.
        // Envelopes are already unpacked, so the result can be used directly.
        //
        // Never throws: an empty dictionary means either "no save" or "could not
        // read". There is no need to tell them apart — there is nothing to take
        // in either case.
        Task<IReadOnlyDictionary<string, CloudSaveEntry>> PeekAccountAsync(string accountId);
    }
}
