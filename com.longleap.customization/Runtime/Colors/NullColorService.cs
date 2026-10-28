using System;
using System.Collections.Generic;
using LongLeap.Skins;
using UnityEngine;

namespace LongLeap.Customization
{
    /// <summary>
    /// Красить нечего: сервиса цветов нет. Заглушка вместо null: чтобы вызывающему не нужны были проверки.
    /// </summary>
    public sealed class NullColorService : IColorService
    {
        private static readonly Dictionary<string, Color> Empty = new Dictionary<string, Color>();
        private static readonly Dictionary<SkinId, Color> EmptySkins = new Dictionary<SkinId, Color>();

        // Пустые аксессоры: событие у заглушки не наступает, подписка ничего не держит.
        public event Action<string, Color> Changed { add { } remove { } }
        public event Action<SkinId, Color> SkinColorChanged { add { } remove { } }

        public IReadOnlyDictionary<string, Color> Colors => Empty;

        public IReadOnlyDictionary<SkinId, Color> SkinColors => EmptySkins;

        public bool TryGetColor(string zone, out Color color)
        {
            color = default;
            return false;
        }

        public void SetColor(string zone, Color color) { }

        public bool TryGetSkinColor(SkinId skin, out Color color)
        {
            color = default;
            return false;
        }

        public void SetSkinColor(SkinId skin, Color color) { }
    }
}
