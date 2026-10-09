using System.Collections.Generic;
using System.Threading.Tasks;

namespace LongLeap.Core.CloudSaves
{
    // A provider that can write and delete many keys in one request.
    //
    // Separate from ICloudSaveProvider because not every provider can: RTDB and
    // the null provider are fine with per-key calls. When a provider implements
    // this interface, CloudSaveService switches its push mode: a write no longer
    // waits for the network, and dirty keys accumulate and go out as a batch —
    // otherwise the first sign-in with hundreds of stats and achievement keys
    // turned into hundreds of requests in a row.
    //
    // Values arrive already unpacked (value + timestamp), not as an envelope:
    // a batch backend doesn't need the envelope, it stores the timestamp as a
    // separate field.
    public interface ICloudSaveBatchProvider
    {
        // Write entries and delete deletes in one go. Throws on failure — then
        // no key from the batch is considered delivered.
        Task SetManyAsync(
            IReadOnlyDictionary<string, CloudSaveEntry> entries,
            IReadOnlyCollection<string> deletes);
    }
}
