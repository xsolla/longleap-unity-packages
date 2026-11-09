using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LongLeap.UI
{
    /// <summary>
    /// Connects UI buttons, including ones created after the UI root has initialized,
    /// to the click sound and press feedback, and <see cref="IHoverSound"/> buttons to the hover sound.
    /// </summary>
    public sealed class MenuClickAudioBinder : MonoBehaviour
    {
        private readonly List<Button> _buttons = new List<Button>();
        private readonly HashSet<Button> _bound = new HashSet<Button>();
        private IUiSounds _sounds;

        public void Bind(IUiSounds sounds)
        {
            _sounds = sounds;
            BindButtons();
        }

        private void Awake() => BindButtons();

        private void LateUpdate() => BindButtons();

        private void BindButtons()
        {
            _bound.RemoveWhere(button => button == null);
            GetComponentsInChildren(true, _buttons);
            foreach (var button in _buttons)
            {
                if (!_bound.Add(button)) continue;

                if (button.TryGetComponent<IHoverSound>(out var hover))
                    hover.PlayHover = PlayHover;

                // Buttons with their own sounds (reward cards) stay silent here.
                if (button.GetComponent<IOwnClickSound>() != null) continue;

                button.onClick.AddListener(PlayClick);

                if (button.GetComponent<MenuButtonPressFeedback>() == null)
                    button.gameObject.AddComponent<MenuButtonPressFeedback>();
            }
        }

        private void PlayClick() => _sounds?.Play(UiSound.Click);

        private void PlayHover() => _sounds?.Play(UiSound.Hover);

        private void OnDestroy()
        {
            foreach (var button in _bound)
                if (button != null) button.onClick.RemoveListener(PlayClick);
        }
    }
}
