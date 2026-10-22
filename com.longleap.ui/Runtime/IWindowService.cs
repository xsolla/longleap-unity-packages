using System;
using System.Threading.Tasks;

namespace LongLeap.UI
{
    /// <summary>
    /// Сервис окон: открывает и закрывает окна, а ещё решает, когда окно создаётся
    /// и когда выгружается — это задаёт <see cref="WindowEntry"/> в каталоге сервиса.
    /// Окно адресуется своим типом: <c>OpenAsync&lt;SettingsWindow&gt;()</c>.
    /// Реализация — <see cref="WindowService"/>.
    ///
    /// Раз окно может ещё не существовать (создаётся при открытии) или уже не
    /// существовать (выгружено при закрытии), экземпляр нельзя считать вечным:
    /// <list type="bullet">
    /// <item>подготовить окно данными — <see cref="OnCreated{TWindow}"/>: сработает на
    /// каждый созданный экземпляр, до показа;</item>
    /// <item>получить экземпляр, не показывая, — <see cref="LoadAsync{TWindow}"/>;</item>
    /// <item><see cref="TryGet{TWindow}"/> отвечает только за уже созданные окна.</item>
    /// </list>
    /// </summary>
    public interface IWindowService
    {
        /// <summary>Окно есть в каталоге сервиса (создано оно сейчас или нет — неважно).</summary>
        bool IsRegistered<TWindow>() where TWindow : class, IWindow;

        /// <summary>
        /// Окно, если оно создано прямо сейчас. Для окон, которые выгружаются или
        /// создаются при открытии, вернёт <c>false</c> в промежутках между показами —
        /// объектам сцены, которым окно нужно синхронно, оно должно быть Preload.
        /// </summary>
        bool TryGet<TWindow>(out TWindow window) where TWindow : class, IWindow;

        /// <summary>
        /// Создать окно (если ещё нет), не открывая, и вернуть экземпляр: положить
        /// в него данные или подписаться на события до показа. Кидает исключение, если окна
        /// нет в каталоге или префаб не загрузился.
        /// </summary>
        Task<TWindow> LoadAsync<TWindow>() where TWindow : class, IWindow;

        /// <summary>
        /// Выгрузить окно сейчас, не дожидаясь правила: закрыть, если открыто,
        /// уничтожить экземпляр и отпустить префаб.
        /// </summary>
        Task UnloadAsync<TWindow>() where TWindow : class, IWindow;

        /// <summary>
        /// Подготовка окна: <paramref name="setup"/> вызывается для уже созданного экземпляра
        /// сразу, а для созданных позже — каждый раз сразу после создания, до открытия.
        /// Так данные доходят до окна, какое бы правило жизни ему ни задали: выгруженное
        /// окно при следующем открытии снова родится «пустым», и подготовка повторится.
        /// Окна нет в каталоге — пишет ошибку в лог и ничего не делает.
        /// </summary>
        void OnCreated<TWindow>(Action<TWindow> setup) where TWindow : class, IWindow;

        /// <summary>Открыть окно (создав, если нужно) и дождаться его анимации появления.</summary>
        Task<TWindow> OpenAsync<TWindow>() where TWindow : class, IWindow;

        /// <summary>
        /// Закрыть окно и дождаться его анимации ухода. Выгрузится ли оно после этого,
        /// решает <see cref="WindowEntry.UnloadOnClose"/>.
        /// </summary>
        Task CloseAsync<TWindow>() where TWindow : class, IWindow;

        /// <summary>
        /// То же по экземпляру — когда тип известен только в рантайме:
        /// например, окно закрывает само себя (<see cref="WindowBase.CloseSelfAsync"/>).
        /// </summary>
        Task CloseAsync(IWindow window);

        Task CloseAllAsync();

        bool IsOpen<TWindow>() where TWindow : class, IWindow;

        /// <summary>
        /// Задать домашнее окно — куда возвращаются страницы меню по «назад»
        /// (<see cref="MenuPageWindow"/>). Обычно главное меню; задаёт игра при загрузке.
        /// </summary>
        void SetHome<TWindow>() where TWindow : class, IWindow;

        /// <summary>Открыть домашнее окно. Не задано — предупреждение в лог, ничего не открывается.</summary>
        Task OpenHomeAsync();
    }
}
