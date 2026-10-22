using System;
using System.Threading.Tasks;
using UnityEngine;

namespace LongLeap.UI
{
    /// <summary>
    /// База для окон.
    ///
    /// Окно — отдельный префаб, на корне которого этот компонент; сервис создаёт его сам
    /// (см. <see cref="WindowEntry"/>) и может уничтожить после закрытия, поэтому живёт
    /// окно не вечно: ни на что внутри него не стоит рассчитывать между показами.
    ///
    /// Объект самого окна включён всегда: на нём живёт компонент, поэтому окно может
    /// держать ссылки, слушать события и считать что-то, пока закрыто. Включается и
    /// гаснет только <see cref="Root"/> — дочерний объект со всем содержимым окна:
    /// канвасом, графикой, кнопками.
    ///
    /// Иерархия окна в префабе UI:
    /// <code>
    /// IntroWindow   (компонент окна, всегда включён)
    /// └── Root      (Canvas и всё содержимое, включается на открытии)
    ///     ├── ...
    /// </code>
    ///
    /// Нужна анимация — переопредели <see cref="OpenAsync"/>/<see cref="CloseAsync"/>:
    /// на открытии сначала <c>base.OpenAsync()</c>, потом проигрыш появления;
    /// на закрытии наоборот — сначала анимация ухода, и только потом <c>base.CloseAsync()</c>,
    /// иначе гасить будет нечего. Сервис дождётся обоих.
    /// </summary>
    public abstract class WindowBase : MonoBehaviour, IWindow
    {
        [Tooltip("Корень содержимого окна: включается при открытии, гаснет при закрытии.")]
        [SerializeField] private GameObject _root;

        /// <summary>
        /// Содержимое окна. Никогда не null: если ссылку забыли проставить,
        /// окно управляет собственным объектом — работать будет, но компонент
        /// вместе с содержимым будет гаснуть.
        /// </summary>
        protected GameObject Root => _root != null ? _root : gameObject;

        /// <summary>Содержимое включено прямо сейчас.</summary>
        public bool IsOpen => Root.activeSelf;

        /// <summary>
        /// Сервис, в котором это окно зарегистрировано. Проставляется при создании окна
        /// (после Awake, до первого показа), поэтому окно может открыть следующее за собой,
        /// не зная, кто его открыл:
        /// <c>Windows.OpenAsync&lt;NextWindow&gt;()</c>.
        /// </summary>
        protected IWindowService Windows { get; private set; }

        /// <summary>Зовёт только <see cref="WindowService"/> при создании окна.</summary>
        internal void Bind(IWindowService service) => Windows = service;

        /// <summary>Закрыть себя — когда окно уводит игрока дальше и уходит следом.</summary>
        protected Task CloseSelfAsync() => Windows != null ? Windows.CloseAsync(this) : Task.CompletedTask;

        /// <summary>
        /// Запустить переход из обработчика кнопки: у <c>onClick</c> нет await,
        /// а молча проглатывать исключение нельзя — иначе окно «не откроется»
        /// без единого следа в логе.
        /// </summary>
        protected static async void Forget(Task task)
        {
            try
            {
                await task;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        public virtual Task OpenAsync()
        {
            Root.SetActive(true);
            return Task.CompletedTask;
        }

        public virtual Task CloseAsync()
        {
            Root.SetActive(false);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Закрыть без анимации и без ожидания. Нужно сервису при создании окна: начальное
        /// состояние окон задаёт код, а не то, каким сохранили префаб.
        /// </summary>
        public void CloseImmediate()
        {
            Root.SetActive(false);
        }

        protected virtual void Awake()
        {
            if (_root == null)
                Debug.LogError(
                    $"[Windows] у окна {GetType().Name} не задан Root — гаснуть будет сам объект окна.",
                    this);
        }
    }
}
