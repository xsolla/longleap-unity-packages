using System;
using System.Collections;
using LongLeap.Notifications;
using TMPro;
using UnityEngine;

namespace LongLeap.UI
{
    /// <summary>
    /// One toast. With an Animator that has a controller, the "Show"/"Hide" triggers drive it and
    /// the hide clip must call <see cref="OnHideAnimationComplete"/>. Without one the popup fades
    /// its CanvasGroup in code, so the prefab works with no animation assets at all.
    /// </summary>
    public class NotificationPopup : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI messageText;
        [SerializeField] private Animator animator;

        [Header("Fallback fade (no Animator controller)")]
        [Min(0f)] [SerializeField] private float fadeSeconds = 0.25f;

        private Action _onHidden;
        private bool _isHiding;
        private static readonly int ShowTrigger = Animator.StringToHash("Show");
        private static readonly int HideTrigger = Animator.StringToHash("Hide");

        private bool UsesAnimator => animator != null && animator.runtimeAnimatorController != null;

        public void Show(NotificationData data, Action onHidden)
        {
            _onHidden = onHidden;

            if (titleText != null)
                titleText.text = data.Title;
            if (messageText != null)
                messageText.text = data.Message;
            if (canvasGroup != null)
                canvasGroup.alpha = 0f;

            if (UsesAnimator)
                animator.SetTrigger(ShowTrigger);
            else
                StartCoroutine(Fade(0f, 1f, null));
        }

        public void Hide()
        {
            if (_isHiding) return;
            _isHiding = true;

            if (UsesAnimator)
                animator.SetTrigger(HideTrigger);
            else
                StartCoroutine(Fade(canvasGroup != null ? canvasGroup.alpha : 1f, 0f, OnHideAnimationComplete));
        }

        public void OnHideAnimationComplete()
        {
            _onHidden?.Invoke();
            _onHidden = null;

            Destroy(gameObject);
        }

        private IEnumerator Fade(float from, float to, Action done)
        {
            if (canvasGroup != null && fadeSeconds > 0f)
            {
                // Unscaled: toasts also show while the game is paused.
                for (float t = 0f; t < fadeSeconds; t += Time.unscaledDeltaTime)
                {
                    canvasGroup.alpha = Mathf.Lerp(from, to, t / fadeSeconds);
                    yield return null;
                }
            }

            if (canvasGroup != null)
                canvasGroup.alpha = to;

            done?.Invoke();
        }
    }
}
