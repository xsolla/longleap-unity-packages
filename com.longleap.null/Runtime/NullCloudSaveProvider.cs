using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Core.CloudSaves;

namespace LongLeap.Null
{
    // Область сейва здесь тоже заглушка, и это не пробел: писать заглушке
    // некуда, поэтому и разделять гостевую ветвь с аккаунтной незачем.
    // Переключатель сейвов от этого не ломается — он видит пустой сейв аккаунта
    // и молча оставляет игроку то, что уже есть.
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
