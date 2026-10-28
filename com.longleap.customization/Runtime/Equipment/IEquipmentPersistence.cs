using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Skins;

namespace LongLeap.Customization
{
    /// <summary>
    /// Где лежит надетое. Зачем отдельный интерфейс — см.
    /// <see cref="IWalletPersistence"/>.
    ///
    /// Пишется по категориям, а не одним списком: переодевание меняет ровно одну
    /// категорию, и писать из-за неё весь набор незачем.
    /// </summary>
    public interface IEquipmentPersistence
    {
        /// <summary>Записать, что надето в категории.</summary>
        Task SaveAsync(SkinCategoryId category, IReadOnlyList<SkinId> skins);

        /// <summary>
        /// Прочитать надетое в категории. null — записи нет (первый запуск),
        /// пустой список — игрок снял все скины этой категории.
        /// </summary>
        Task<IReadOnlyList<SkinId>> LoadAsync(SkinCategoryId category);
    }
}
