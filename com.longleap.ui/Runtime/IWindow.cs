using System.Threading.Tasks;

namespace LongLeap.UI
{
    /// <summary>
    /// Контракт любого окна. Открытие и закрытие асинхронные — чтобы окно могло
    /// проиграть анимацию появления/ухода, а сервис дождался её конца.
    /// Готовая реализация по умолчанию — <see cref="WindowBase"/>.
    /// </summary>
    public interface IWindow
    {
        Task OpenAsync();

        Task CloseAsync();
    }
}
