using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LongLeap.UI
{
    [RequireComponent(typeof(Button))]
    [DisallowMultipleComponent]
    public sealed class MenuButtonPressFeedback : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, ISubmitHandler
    {
        [SerializeField, Range(0.8f, 1f)] private float _pressedScale = 0.96f;
        [SerializeField, Min(0f)] private float _duration = 0.06f;
        private Button _button;
        private Vector3 _baseScale;
        private Coroutine _animation;
        private bool _initialized;
        private bool _pressed;

        private void Awake()
        {
            _button = GetComponent<Button>();
            _baseScale = transform.localScale;
            _initialized = true;
        }

        public void OnPointerDown(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left ||
                !_button.IsActive() || !_button.IsInteractable()) return;
            _pressed = true;
            AnimateTo(_pressedScale);
        }

        public void OnPointerUp(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left) Release();
        }

        public void OnPointerExit(PointerEventData data) => Release();

        public void OnSubmit(BaseEventData data)
        {
            if (!_button.IsActive() || !_button.IsInteractable() || !isActiveAndEnabled) return;
            StopAnimation();
            _animation = StartCoroutine(SubmitPulse());
        }

        private IEnumerator SubmitPulse()
        {
            yield return ScaleTo(_baseScale * _pressedScale);
            yield return ScaleTo(_baseScale);
            _animation = null;
        }

        private void Release()
        {
            if (!_pressed) return;
            _pressed = false;
            AnimateTo(1f);
        }

        private void AnimateTo(float multiplier)
        {
            StopAnimation();
            if (isActiveAndEnabled)
                _animation = StartCoroutine(ScaleTo(_baseScale * multiplier));
            else if (_initialized) transform.localScale = _baseScale;
        }

        private IEnumerator ScaleTo(Vector3 destination)
        {
            var start = transform.localScale;
            for (float elapsed = 0f; elapsed < _duration; elapsed += Time.unscaledDeltaTime)
            {
                float t = elapsed / _duration;
                transform.localScale = Vector3.Lerp(start, destination, t * t * (3f - 2f * t));
                yield return null;
            }
            transform.localScale = destination;
        }

        private void StopAnimation()
        {
            if (_animation != null) StopCoroutine(_animation);
            _animation = null;
        }

        private void LateUpdate()
        {
            if (_pressed && !_button.IsInteractable()) ResetScale();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) ResetScale();
        }

        private void OnDisable() => ResetScale();

        private void ResetScale()
        {
            StopAnimation();
            _pressed = false;
            if (_initialized) transform.localScale = _baseScale;
        }
    }
}
