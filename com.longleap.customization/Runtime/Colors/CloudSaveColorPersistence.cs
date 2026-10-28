using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Core.CloudSaves;
using LongLeap.Skins;
using UnityEngine;

namespace LongLeap.Customization
{
    /// <summary>
    /// Хранит цвета зон в облачных сейвах — там же, где надетое, чтобы «покрасил на
    /// телефоне» не разъехалось с «покрасил на вебе».
    ///
    /// Один ключ на все зоны сразу: их немного, категорий у них нет, а писать файл
    /// заново на каждый чих палитры — не дороже, чем у надетого на одну категорию.
    /// В том же ключе — цвета отдельных скинов (список <c>skins</c>): так их чистит тот
    /// же чит, а сейв без этого списка (старый) читается как «своих цветов у скинов нет».
    /// </summary>
    public sealed class CloudSaveColorPersistence : IColorPersistence
    {
        private const string Key = "player_colors";

        [Serializable]
        private sealed class SavedZoneColor
        {
            public string zone;
            public Color color;
        }

        [Serializable]
        private sealed class SavedSkinColor
        {
            public string skin;
            public Color color;
        }

        [Serializable]
        private sealed class SavedColors
        {
            public List<SavedZoneColor> zones = new List<SavedZoneColor>();
            public List<SavedSkinColor> skins = new List<SavedSkinColor>();
        }

        private readonly ICloudSaveService _cloudSave;

        public CloudSaveColorPersistence(ICloudSaveService cloudSave)
        {
            _cloudSave = cloudSave ?? throw new ArgumentNullException(nameof(cloudSave));
        }

        /// <summary>
        /// Ключ сейва. Публичный: по нему чит из меню Tools чистит цвета, и знать
        /// формат в двух местах не нужно.
        /// </summary>
        public static string SaveKey => Key;

        public Task SaveAsync(ColorSnapshot colors)
        {
            var saved = new SavedColors();

            if (colors != null)
            {
                foreach (var entry in colors.Zones)
                    saved.zones.Add(new SavedZoneColor { zone = entry.Key, color = entry.Value });

                foreach (var entry in colors.Skins)
                    saved.skins.Add(new SavedSkinColor { skin = entry.Key.Value, color = entry.Value });
            }

            return _cloudSave.SetStringAsync(new CloudSaveKey(Key), JsonUtility.ToJson(saved));
        }

        public async Task<ColorSnapshot> LoadAsync()
        {
            var raw = await _cloudSave.GetStringAsync(new CloudSaveKey(Key));

            if (string.IsNullOrWhiteSpace(raw))
                return null;

            try
            {
                var saved = JsonUtility.FromJson<SavedColors>(raw);
                if (saved?.zones == null) return null;

                var colors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in saved.zones)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.zone)) continue;
                    colors[entry.zone] = entry.color;
                }

                var skins = new Dictionary<SkinId, Color>();
                if (saved.skins != null)
                    foreach (var entry in saved.skins)
                    {
                        if (entry == null || string.IsNullOrWhiteSpace(entry.skin)) continue;
                        try { skins[new SkinId(entry.skin)] = entry.color; }
                        catch (ArgumentException exception)
                        {
                            Debug.LogWarning($"[Player] в сейве цветов битое имя скина: {exception.Message}");
                        }
                    }

                return new ColorSnapshot(colors, skins);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Player] не удалось прочитать сохранённые цвета: {exception.Message}");
                return null;
            }
        }
    }
}
