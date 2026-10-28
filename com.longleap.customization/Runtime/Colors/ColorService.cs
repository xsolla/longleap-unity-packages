using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Skins;
using UnityEngine;

namespace LongLeap.Customization
{
    /// <summary>
    /// Цвета зон, которые выбрал игрок: одна запись на зону, без категорий, — и
    /// отдельно цвета скинов из групп с множественным выбором.
    /// </summary>
    public sealed class ColorService : IColorService
    {
        private readonly IColorPersistence _persistence;
        private readonly Dictionary<string, Color> _colors =
            new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<SkinId, Color> _skinColors = new Dictionary<SkinId, Color>();

        private bool _dirty;
        private bool _saving;

        public ColorService(IColorPersistence persistence = null)
        {
            _persistence = persistence;
        }

        public event Action<string, Color> Changed;
        public event Action<SkinId, Color> SkinColorChanged;

        public IReadOnlyDictionary<string, Color> Colors => _colors;

        public IReadOnlyDictionary<SkinId, Color> SkinColors => _skinColors;

        public bool TryGetColor(string zone, out Color color)
        {
            if (!string.IsNullOrEmpty(zone) && _colors.TryGetValue(zone, out color))
                return true;

            color = default;
            return false;
        }

        public void SetColor(string zone, Color color)
        {
            if (string.IsNullOrEmpty(zone)) return;

            _colors[zone] = color;
            SaveSoon();
            RaiseChanged(zone, color);
        }

        public bool TryGetSkinColor(SkinId skin, out Color color)
        {
            if (!skin.IsNone && _skinColors.TryGetValue(skin, out color))
                return true;

            color = default;
            return false;
        }

        public void SetSkinColor(SkinId skin, Color color)
        {
            if (skin.IsNone) return;

            _skinColors[skin] = color;
            SaveSoon();

            try { SkinColorChanged?.Invoke(skin, color); }
            catch (Exception e)
            {
                Debug.LogError($"[Player] подписчик SkinColorChanged кинул исключение: {e}");
            }
        }

        /// <summary>
        /// В сейве нашлись цвета — игрок их уже задавал (или их задали ему стартовым
        /// набором в прошлый запуск). По нему загрузка решает, красить ли новичка
        /// цветами из Skin Layout Editor (так делает загрузка хоста).
        /// </summary>
        public bool HasSavedColors { get; private set; }

        /// <summary>Прочитать сохранённое. Зовётся один раз при сборке игрока.</summary>
        public async Task LoadAsync()
        {
            if (_persistence == null) return;

            var loaded = await _persistence.LoadAsync();
            if (loaded == null) return;

            HasSavedColors = true;

            _colors.Clear();
            foreach (var entry in loaded.Zones)
                _colors[entry.Key] = entry.Value;

            _skinColors.Clear();
            foreach (var entry in loaded.Skins)
                _skinColors[entry.Key] = entry.Value;
        }

        /// <summary>
        /// Дописать несохранённое. Пока предыдущая запись в полёте, новый вызов
        /// просто отмечается — текущий цикл сохранит уже актуальный снимок сам.
        /// </summary>
        public async Task FlushAsync()
        {
            if (_persistence == null || _saving) return;

            _saving = true;
            try
            {
                do
                {
                    _dirty = false;
                    var snapshot = new ColorSnapshot(
                        new Dictionary<string, Color>(_colors, StringComparer.OrdinalIgnoreCase),
                        new Dictionary<SkinId, Color>(_skinColors));

                    try
                    {
                        await _persistence.SaveAsync(snapshot);
                    }
                    catch (Exception e)
                    {
                        _dirty = true;
                        Debug.LogWarning($"[Player] не удалось сохранить цвета: {e.Message}");
                        break;
                    }
                } while (_dirty);
            }
            finally
            {
                _saving = false;
            }
        }

        private void SaveSoon()
        {
            if (_persistence == null) return;

            _dirty = true;
            _ = FlushAsync();
        }

        private void RaiseChanged(string zone, Color color)
        {
            try { Changed?.Invoke(zone, color); }
            catch (Exception e)
            {
                Debug.LogError($"[Player] подписчик Changed (цвет) кинул исключение: {e}");
            }
        }
    }
}
