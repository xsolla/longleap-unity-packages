using System;
using System.Collections.Generic;
using LongLeap.Skins;

namespace LongLeap.Customization
{
    /// <summary>
    /// Надевать нечего: инвентаря нет, значит нет и категорий. Зачем заглушка
    /// вместо null — чтобы вызывающему не нужны были проверки.
    ///
    /// Про неудачное <see cref="Equip"/> молчит, в отличие от кошелька и
    /// инвентаря: сюда приходят нажатия игрока по вариантам, а не код выдачи
    /// наград, и предупреждение в консоль тут означало бы строчку на каждый
    /// клик.
    /// </summary>
    public sealed class NullEquipmentService : IEquipmentService
    {
        private static readonly SkinId[] Nothing = new SkinId[0];

        // Пустые аксессоры: событие у заглушки не наступает, подписка ничего не держит.
        public event Action<SkinCategoryId, SkinId> Changed { add { } remove { } }

        public IReadOnlyList<SkinId> Outfit => Nothing;

        public SkinId GetEquipped(SkinCategoryId category) => default;

        public bool IsEquipped(SkinId skin) => false;

        public bool Equip(SkinId skin) => false;

        public bool Unequip(SkinId skin) => false;
    }
}
