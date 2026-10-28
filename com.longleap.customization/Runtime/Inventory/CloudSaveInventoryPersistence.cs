using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Core.CloudSaves;
using LongLeap.Skins;
using UnityEngine;

namespace LongLeap.Customization
{
    /// <summary>
    /// Хранит открытые скины в облачных сейвах — там же, где статы, ачивки и
    /// балансы, поэтому купленный скин не может отстать от списанных за него
    /// денег при переезде на другое устройство.
    ///
    /// Один ключ <c>inventory_skins</c>, значение — имена скинов через перевод
    /// строки. Разделитель именно такой, потому что <see cref="SkinId"/>
    /// запрещает перевод строки внутри имени: любой другой разделитель
    /// (запятая, точка с запятой) рано или поздно встретился бы в имени скина
    /// из спайна и развалил бы список.
    /// </summary>
    public sealed class CloudSaveInventoryPersistence : IInventoryPersistence
    {
        /// <summary>Ключ в облачных сейвах. Публичный: его чистит чит из меню Tools.</summary>
        public const string SaveKey = "inventory_skins";
        private const char Separator = '\n';

        private static readonly SkinId[] Nothing = new SkinId[0];

        private readonly ICloudSaveService _cloudSave;

        public CloudSaveInventoryPersistence(ICloudSaveService cloudSave)
        {
            _cloudSave = cloudSave ?? throw new ArgumentNullException(nameof(cloudSave));
        }

        public async Task<IReadOnlyList<SkinId>> LoadAsync()
        {
            var raw = await _cloudSave.GetStringAsync(new CloudSaveKey(SaveKey));

            if (string.IsNullOrEmpty(raw))
                return Nothing;

            var parts = raw.Split(Separator);
            var skins = new List<SkinId>(parts.Length);

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (string.IsNullOrWhiteSpace(part)) continue;

                try
                {
                    skins.Add(new SkinId(part));
                }
                catch (ArgumentException exception)
                {
                    // Правили руками или разъехался формат. Один битый скин не
                    // повод терять остальные: пропускаем именно его.
                    Debug.LogWarning($"[Player] в сейве инвентаря битое имя скина: {exception.Message}");
                }
            }

            return skins;
        }

        public Task SaveAsync(IReadOnlyCollection<SkinId> skins)
        {
            if (skins == null) throw new ArgumentNullException(nameof(skins));

            var names = new List<string>(skins.Count);
            foreach (var skin in skins)
            {
                if (skin.IsNone) continue;
                names.Add(skin.Value);
            }

            return _cloudSave.SetStringAsync(new CloudSaveKey(SaveKey), string.Join(Separator.ToString(), names));
        }
    }
}
