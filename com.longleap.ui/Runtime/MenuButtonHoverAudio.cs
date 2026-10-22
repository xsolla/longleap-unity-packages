using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LongLeap.UI
{
    [RequireComponent(typeof(Button))]
    public sealed class MenuButtonHoverAudio : MonoBehaviour, IPointerEnterHandler
    {
        public Action PlaySound;

        public void OnPointerEnter(PointerEventData eventData)
        {
            var button = GetComponent<Button>();
            if (button.IsActive() && button.IsInteractable())
                PlaySound?.Invoke();
        }
    }
}
