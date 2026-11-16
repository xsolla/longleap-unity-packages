using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace LongLeap.UI
{
    /// <summary>
    /// A main menu page: settings, achievements, rewards.
    ///
    /// They have exactly one thing in common — the "back" button: the page closes itself and asks
    /// the service to bring back the home window (<see cref="IWindowService.SetHome{TWindow}"/>,
    /// set by the game — usually the main menu). All other content lives
    /// in subclasses, each of which is a separate type, because the type is the window's address
    /// in <see cref="IWindowService"/>.
    /// </summary>
    public abstract class MenuPageWindow : WindowBase
    {
        [Tooltip("'Back' button: closes the page and returns to the main menu.")]
        [SerializeField] private Button _backButton;

        /// <summary>
        /// Return to the main menu. A page whose "back" doesn't lead to the menu
        /// (see <see cref="UserBackPackWindow"/>) overrides this.
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
