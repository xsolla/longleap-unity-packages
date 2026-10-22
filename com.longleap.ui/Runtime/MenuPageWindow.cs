using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace LongLeap.UI
{
    /// <summary>
    /// Страница главного меню: настройки, ачивки, награды.
    ///
    /// Общего у них ровно одно — кнопка «назад»: страница закрывает себя и просит
    /// сервис вернуть домашнее окно (<see cref="IWindowService.SetHome{TWindow}"/>,
    /// его задаёт игра — обычно главное меню). Всё остальное наполнение живёт
    /// в наследниках, каждый из которых — отдельный тип, потому что тип и есть адрес
    /// окна в <see cref="IWindowService"/>.
    /// </summary>
    public abstract class MenuPageWindow : WindowBase
    {
        [Tooltip("Кнопка «назад»: закрывает страницу и возвращает главное меню.")]
        [SerializeField] private Button _backButton;

        /// <summary>
        /// Вернуться в главное меню. Страница, у которой «назад» ведёт не в меню
        /// (см. <see cref="UserBackPackWindow"/>), переопределяет это.
        /// </summary>
        public virtual async Task BackAsync()
        {
            if (Windows == null) return;

            await Windows.OpenHomeAsync();
            await CloseSelfAsync();
        }

        protected override void Awake()
        {
            base.Awake();

            if (_backButton != null)
                _backButton.onClick.AddListener(OnBack);
        }

        protected virtual void OnDestroy()
        {
            if (_backButton != null)
                _backButton.onClick.RemoveListener(OnBack);
        }

        private void OnBack() => Forget(BackAsync());
    }
}
