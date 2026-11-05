using System.Threading.Tasks;

namespace LongLeap.UI
{
    /// <summary>
    /// Contract for any window. Opening and closing are asynchronous — so that the window can
    /// play an appear/exit animation and the service can wait for it to finish.
    /// Ready-made default implementation — <see cref="WindowBase"/>.
    /// </summary>
    public interface IWindow
    {
        Task OpenAsync();

        Task CloseAsync();
    }
}
