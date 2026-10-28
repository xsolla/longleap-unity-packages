using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Skins;
using UnityEngine;

namespace LongLeap.Customization
{
    /// <summary>
    /// Где лежат цвета зон и отдельных скинов. Зачем отдельный интерфейс — см.
    /// <see cref="IWalletPersistence"/>.
    ///
    /// Пишется целиком, в отличие от надетого по категориям: цветов немного, а
    /// категорий у них нет вовсе — делить запись не на что.
    /// </summary>
    public interface IColorPersistence
    {
        /// <summary>Записать все заданные цвета разом.</summary>
        Task SaveAsync(ColorSnapshot colors);

        /// <summary>
        /// Прочитать сохранённые цвета. null — записи нет (первый запуск или сейв не
        /// поднялся): персонаж останется в цветах по умолчанию из инспектора.
        /// </summary>
        Task<ColorSnapshot> LoadAsync();
    }

    /// <summary>
    /// Все цвета игрока разом: по цветовым зонам и по
    /// отдельным скинам групп с множественным выбором (см. <see cref="IColorService"/>).
    /// </summary>
    public sealed class ColorSnapshot
    {
        public ColorSnapshot(IReadOnlyDictionary<string, Color> zones, IReadOnlyDictionary<SkinId, Color> skins)
        {
            Zones = zones ?? new Dictionary<string, Color>();
            Skins = skins ?? new Dictionary<SkinId, Color>();
        }

        public IReadOnlyDictionary<string, Color> Zones { get; }

        public IReadOnlyDictionary<SkinId, Color> Skins { get; }
    }
}
