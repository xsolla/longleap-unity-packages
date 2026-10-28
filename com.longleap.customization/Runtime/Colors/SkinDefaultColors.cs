using LongLeap.Skins;

namespace LongLeap.Customization
{
    /// <summary>
    /// Default Color скина (Skin Layout Editor) → сервис цветов. Одно правило на два
    /// места: панель кастомизации зовёт его, когда игрок надевает скин, а загрузка —
    /// когда одевает нового игрока в стартовый набор (так делает загрузка хоста). Раньше
    /// цвет ложился только по нажатию, и новичок стоял в белом.
    ///
    /// В группе с множественным выбором (аксессуары) цвет ложится на сам скин, иначе —
    /// на зоны его группы: зона у аксессуаров общая, и её цвет перекрасил бы все
    /// надетые разом (см. <see cref="IColorService"/>).
    /// </summary>
    public static class SkinDefaultColors
    {
        /// <summary>
        /// Записать Default Color скина в сервис цветов. false — у скина своего цвета нет
        /// (или нечем/некуда писать): зоны остаются такими, какими были.
        /// </summary>
        public static bool TryApply(IInventoryService inventory, IColorService colors, SkinId skin)
        {
            if (inventory == null || colors == null || skin.IsNone) return false;
            if (!inventory.TryGetDefaultColor(skin, out var color)) return false;

            var group = GroupOf(skin);

            if (inventory.IsGroupMultiSelect(group))
            {
                colors.SetSkinColor(skin, color);
                return true;
            }

            var zones = inventory.GetColorZones(group);

            for (var i = 0; i < zones.Count; i++)
                colors.SetColor(zones[i], color);

            return true;
        }

        private static string GroupOf(SkinId skin)
        {
            var value = skin.Value;
            var slash = value.LastIndexOf('/');
            return slash < 0 ? value : value.Substring(0, slash);
        }
    }
}
