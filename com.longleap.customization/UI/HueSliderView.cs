using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LongLeap.Customization
{
    /// <summary>
    /// Полоска оттенка для <see cref="ColorPaletteView"/> (в префабе — «colorSlider»):
    /// по её длине водят ручку, а радугу рисует материал шейдера
    /// <c>LongLeap/Customization/HueStrip</c>. Как и у квадрата палитры, оттенок не читается с
    /// картинки, а считается прямо из позиции ручки.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HueSliderView : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [Tooltip("Полоска радуги: по ней водят ручку. Пусто — берётся Image с этого же объекта.")]
        [SerializeField] private Image _stripImage;

        [Tooltip("Ручка полоски. Пусто — оттенок будет переключаться, но метка не двигается.")]
        [SerializeField] private RectTransform _handle;

        [Tooltip("Полоска лежит горизонтально. Выключено — вертикально (оттенок читается по высоте).")]
        [SerializeField] private bool _horizontal;

        [Tooltip("Квадрат палитры, которому эта полоска задаёт оттенок.")]
        [SerializeField] private ColorPaletteView _palette;

        public void OnPointerDown(PointerEventData eventData) => PickAt(eventData);
        public void OnDrag(PointerEventData eventData) => PickAt(eventData);
        public void OnPointerUp(PointerEventData eventData) => PickAt(eventData);

        private void PickAt(PointerEventData eventData)
        {
            var image = ResolveStripImage();
            if (image == null) return;

            var rect = image.rectTransform;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rect, eventData.position, eventData.pressEventCamera, out var local))
                return;

            float t;

            if (_horizontal)
            {
                var width = rect.rect.width;
                if (width <= 0f) return;

                t = Mathf.Clamp01((local.x - rect.rect.x) / width);
            }
            else
            {
                var height = rect.rect.height;
                if (height <= 0f) return;

                t = Mathf.Clamp01((local.y - rect.rect.y) / height);
            }

            PlaceHandle(rect, t);

            if (_palette != null)
                _palette.SetHue(t);
        }

        private void PlaceHandle(RectTransform rect, float t)
        {
            if (_handle == null) return;

            _handle.anchoredPosition = _horizontal
                ? new Vector2(rect.rect.x + t * rect.rect.width, _handle.anchoredPosition.y)
                : new Vector2(_handle.anchoredPosition.x, rect.rect.y + t * rect.rect.height);
        }

        private void OnEnable()
        {
            var image = ResolveStripImage();
            if (image != null && _palette != null)
                PlaceHandle(image.rectTransform, _palette.Hue);
        }

        private Image ResolveStripImage()
        {
            if (_stripImage == null)
                _stripImage = GetComponent<Image>();

            return _stripImage;
        }
    }
}
