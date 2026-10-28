using System;
using System.Collections.Generic;
using LongLeap.Skins;
using UnityEngine;

namespace LongLeap.Customization
{
    /// <summary>
    /// Цвета зон перекраса (зоны задаёт хост), которые выбрал игрок
    /// в палитре кастомизации.
    ///
    /// Отдельно от <see cref="IEquipmentService"/>, потому что зона переживает смену
    /// скина: покрасили «skin» один раз — цвет остаётся, даже если категория
    /// переоделась в другой вариант. Ключ — имя зоны, как в
    /// имя зоны в конфиге внешнего вида хоста (регистр не важен).
    ///
    /// Кроме зон — цвета отдельных скинов: в группе с множественным выбором
    /// (аксессуары) надето несколько вещей с общей зоной, и цвет зоны покрасил бы их
    /// все разом. Цвет скина ложится поверх цвета зоны и только на его слоты.
    /// </summary>
    public interface IColorService
    {
        /// <summary>
        /// Все заданные игроком цвета зон. Список пересобирается при изменениях,
        /// поэтому держать его у себя между кадрами не стоит — спрашивай заново по
        /// <see cref="Changed"/> или на каждой привязке персонажа.
        /// </summary>
        IReadOnlyDictionary<string, Color> Colors { get; }

        /// <summary>Все заданные игроком цвета отдельных скинов — то же, что <see cref="Colors"/>, но по скинам.</summary>
        IReadOnlyDictionary<SkinId, Color> SkinColors { get; }

        /// <summary>Цвет зоны, если игрок его когда-либо задавал.</summary>
        bool TryGetColor(string zone, out Color color);

        /// <summary>Задать цвет зоны. Пустое имя зоны игнорируется.</summary>
        void SetColor(string zone, Color color);

        /// <summary>Цвет скина, если игрок его когда-либо задавал.</summary>
        bool TryGetSkinColor(SkinId skin, out Color color);

        /// <summary>Задать цвет скина. Пустой скин игнорируется.</summary>
        void SetSkinColor(SkinId skin, Color color);

        /// <summary>Цвет зоны сменился — сюда же прилетает и восстановление из сейва.</summary>
        event Action<string, Color> Changed;

        /// <summary>Цвет скина сменился.</summary>
        event Action<SkinId, Color> SkinColorChanged;
    }
}
