using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Core.CloudSaves;
using LongLeap.Skins;
using UnityEngine;

namespace LongLeap.Customization
{
    /// <summary>
    /// Хранит надетое в облачных сейвах — там же, где открытые скины, чтобы
    /// «надел на телефоне» не разъехалось с «купил на вебе».
    ///
    /// Ключ — id категории с префиксом: <c>equipped_board</c>, значение — JSON
    /// со списком надетых скинов. Старое значение с одним именем тоже читается.
    /// </summary>
    public sealed class CloudSaveEquipmentPersistence : IEquipmentPersistence
    {
        private const string KeyFormat = "equipped_{0}";

        [Serializable]
        private sealed class SavedOutfit
        {
            public List<string> skins = new List<string>();
        }

        private readonly ICloudSaveService _cloudSave;

        public CloudSaveEquipmentPersistence(ICloudSaveService cloudSave)
        {
            _cloudSave = cloudSave ?? throw new ArgumentNullException(nameof(cloudSave));
        }

        /// <summary>
        /// Ключ сейва для категории. Публичный: по нему чит из меню Tools чистит
        /// надетое, и знать формат в двух местах не нужно.
        /// </summary>
        public static string SaveKey(SkinCategoryId category) =>
            string.Format(KeyFormat, category.Value);

        public Task SaveAsync(SkinCategoryId category, IReadOnlyList<SkinId> skins)
        {
            var saved = new SavedOutfit();
            if (skins != null)
                for (var i = 0; i < skins.Count; i++)
                    if (!skins[i].IsNone) saved.skins.Add(skins[i].Value);

            return _cloudSave.SetStringAsync(new CloudSaveKey(SaveKey(category)), JsonUtility.ToJson(saved));
        }

        public async Task<IReadOnlyList<SkinId>> LoadAsync(SkinCategoryId category)
        {
            var raw = await _cloudSave.GetStringAsync(new CloudSaveKey(SaveKey(category)));

            if (string.IsNullOrWhiteSpace(raw))
                return null;

            try
            {
                // Старые сейвы содержали одно имя вместо JSON-массива.
                var names = raw.TrimStart().StartsWith("{")
                    ? JsonUtility.FromJson<SavedOutfit>(raw)?.skins
                    : new List<string> { raw };
                if (names == null) return null;

                var skins = new List<SkinId>();
                foreach (var name in names)
                {
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    try { skins.Add(new SkinId(name)); }
                    catch (ArgumentException exception)
                    {
                        Debug.LogWarning($"[Player] в сейве надетого битое имя скина: {exception.Message}");
                    }
                }

                return skins;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Player] не удалось прочитать надетое в '{category}': {exception.Message}");
                return null;
            }
        }
    }
}
