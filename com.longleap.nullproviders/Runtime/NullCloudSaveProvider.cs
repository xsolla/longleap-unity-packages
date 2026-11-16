using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Core.CloudSaves;

namespace LongLeap.Null
{
    /// <summary>
    /// Cloud save backend that keeps nothing: writes are dropped, reads return
    /// empty results. The player's data then lives only in the local cache of
    /// <see cref="CloudSaveService"/> on this device.
    /// </summary>
    /// <remarks>
    /// Guest scope only. <see cref="UseAccount"/> is ignored, so the provider
    /// never reports an account scope. Do not pair it with a signed-in
    /// account: the save switcher would treat every launch as "guest data,
    /// player signed in" and merge again each time.
    /// </remarks>
    // The account scope is a stub on purpose: the provider has nowhere to
    // write, so splitting guest and account data would be pointless. For a
    // guest the save switcher sees no account scope and leaves the local save
    // as it is.
    public sealed class NullCloudSaveProvider : ICloudSaveProvider, IAccountCloudSaveScope
    {
        public Task SetStringAsync(CloudSaveKey key, string value) => Task.CompletedTask;
        public Task<string> GetStringAsync(CloudSaveKey key) => Task.FromResult<string>(null);
        public Task<bool> HasKeyAsync(CloudSaveKey key) => Task.FromResult(false);
        public Task DeleteKeyAsync(CloudSaveKey key) => Task.CompletedTask;
        public Task<Dictionary<string, string>> LoadAllAsync() => Task.FromResult(new Dictionary<string, string>());
        public Task DeleteAllAsync() => Task.CompletedTask;

        public bool IsAccountScoped => false;
        public string AccountId => "";
        public void UseAccount(string accountId) { }
        public void UseGuest() { }

        public Task<IReadOnlyDictionary<string, CloudSaveEntry>> PeekAccountAsync(string accountId) =>
            Task.FromResult<IReadOnlyDictionary<string, CloudSaveEntry>>(new Dictionary<string, CloudSaveEntry>());
    }
}
