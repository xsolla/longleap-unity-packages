using System.Collections.Generic;
using System.Threading.Tasks;

namespace LongLeap.Core.CloudSaves
{
    // A value together with its timestamp — the same data the envelope holds.
    //
    // Timestamp 0 means "time unknown" (the value was written before envelopes
    // existed). Do not show it as a date.
    public readonly struct CloudSaveEntry
    {
        public string Value { get; }
        public long TimestampUnixSeconds { get; }

        public CloudSaveEntry(string value, long timestampUnixSeconds)
        {
            Value = value;
            TimestampUnixSeconds = timestampUnixSeconds;
        }
    }

    // Reading the whole save as one snapshot.
    //
    // Separate from ICloudSaveService on purpose: that interface returns values
    // one at a time, and a save summary would have to enumerate hundreds of keys
    // (stats alone can use a key per entry). Also, only CloudSaveService
    // implements it — null providers and test fakes have no need for bulk reads.
    public interface ICloudSaveBulkAccess
    {
        // Snapshot of what the service considers the current state.
        //
        // Writes nothing and does not touch the network: it reads memory under
        // the same lock as GetStringAsync. Use it to show the player what is in
        // the save WITHOUT applying it or deciding anything on their behalf.
        Task<IReadOnlyDictionary<string, CloudSaveEntry>> SnapshotAsync();
    }
}
