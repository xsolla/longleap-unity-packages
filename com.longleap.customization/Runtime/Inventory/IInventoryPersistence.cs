using System.Collections.Generic;
using System.Threading.Tasks;
using LongLeap.Skins;

namespace LongLeap.Customization
{
    /// <summary>
    /// Где лежат открытые скины. Зачем отдельный интерфейс — см.
    /// <see cref="IWalletPersistence"/>: <see cref="InventoryService"/>
    /// тестируется без Unity, а замена хранилища не трогает логику выдачи.
    ///
    /// Пишется и читается весь список разом, а не по скину: открытых скинов
    /// десятки, а не тысячи, и один ключ в сейве дешевле, чем ключ на каждый
    /// скин. Категории в хранилище не уходят — они в каталоге.
    /// </summary>
    public interface IInventoryPersistence
    {
        /// <summary>
        /// Прочитать открытые скины. Записи нет (первый запуск) или она
        /// испорчена — пустой список: игрок останется в базовом виде, а не
        /// застрянет на экране загрузки.
        /// </summary>
        Task<IReadOnlyList<SkinId>> LoadAsync();

        /// <summary>Записать открытые скины, заменив прошлый список целиком.</summary>
        Task SaveAsync(IReadOnlyCollection<SkinId> skins);
    }
}
