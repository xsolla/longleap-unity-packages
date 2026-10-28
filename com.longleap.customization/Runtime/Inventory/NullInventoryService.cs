using System;
using System.Collections.Generic;
using LongLeap.Skins;
using UnityEngine;

namespace LongLeap.Customization
{
    /// <summary>
    /// Инвентаря нет: ни категорий, ни скинов. Так бывает, если не нашёлся
    /// ассет источника каталога (<c>SkinLayoutSourceSO</c>, пакет
    /// com.longleap.skins.spine) — тогда кастомизации в этом запуске просто не будет.
    ///
    /// Заглушка вместо null: панель
    /// кастомизации перебирает <see cref="Categories"/> без проверок и должна
    /// показать пустой список, а не упасть.
    /// </summary>
    public sealed class NullInventoryService : IInventoryService
    {
        private static readonly SkinCategoryId[] NoCategories = new SkinCategoryId[0];
        private static readonly SkinId[] NoSkins = new SkinId[0];
        private static readonly string[] NoZones = new string[0];

        private bool _warned;

        // Пустые аксессоры: событие у заглушки не наступает, подписка ничего не держит.
        public event Action Changed { add { } remove { } }

        public IReadOnlyList<SkinCategoryId> Categories => NoCategories;

        public IReadOnlyList<SkinId> GetSkins(SkinCategoryId category) => NoSkins;

        public IReadOnlyList<SkinId> GetOwned(SkinCategoryId category) => NoSkins;

        public string GetCategoryTitle(SkinCategoryId category) => category.Value;

        public string GetTitle(SkinId skin) => skin.Value;

        public SkinId GetDefault(SkinCategoryId category) => default;

        public IReadOnlyList<SkinId> GetStartingOutfit(SkinCategoryId category) => NoSkins;

        public bool Has(SkinId skin) => false;

        public IReadOnlyList<string> GetColorZones(string group) => NoZones;

        public bool CanGroupBeOff(string group) => true;

        public bool IsGroupMultiSelect(string group) => false;

        public bool TryGetDefaultColor(SkinId skin, out Color color)
        {
            color = default;
            return false;
        }

        public Sprite GetIcon(SkinId skin) => null;

        public bool TryGetCategory(SkinId skin, out SkinCategoryId category)
        {
            category = default;
            return false;
        }

        public bool Grant(SkinId skin)
        {
            if (!_warned)
            {
                _warned = true;

                Debug.LogWarning(
                    "[Player] инвентарь не подключён (NullInventoryService) — выдача скинов ни к чему " +
                    "не приводит. Проверь, что есть ассет 'Resources/Player/SkinLayoutSource'.");
            }

            return false;
        }
    }
}
