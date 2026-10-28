using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LongLeap.Customization
{
    /// <summary>
    /// Квадратная палитра для перекраса скина: висит поверх панели кастомизации,
    /// прячется по умолчанию (см. <see cref="CustomizationPanel"/> — «color» на
    /// строке группы её открывает под свои зоны).
    ///
    /// Палитра рисуется шейдером (<c>LongLeap/Customization/HSVSquare</c>), а не картинкой: сама
    /// область (<see cref="_paletteImage"/>) — это насыщенность (X) и яркость (Y) при
    /// текущем оттенке, а оттенок приходит с полоски (<see cref="_hueSlider"/>, в
    /// префабе — «colorSlider»). Цвет считается прямо из HSV
    /// (<see cref="Color.HSVToRGB(float,float,float,bool)"/>) — картинку под ручкой
    /// никто не читает, поэтому спрайту палитры Read/Write не нужен.
    ///
    /// Ручка (<see cref="_handle"/>, в префабе — «joystik») тащится любым способом,
    /// каким в проекте вообще жмут по UI (мышь, тач, VR-луч через EventSystem) —
    /// отдельного чтения джойстика контроллера здесь нет и не нужно.
    ///
    /// ЗОН НА ОТКРЫТИЕ МОЖЕТ БЫТЬ НЕСКОЛЬКО, и это сознательно: у художника зоны
    /// перекраса заведены не по одной на группу
    /// скинов, а по смыслу — например, борд разбит на «Skateboard Wheels» и
    /// «Skateboard Grip» отдельно. Одна ручка красит их разом тем же цветом, а какие
    /// именно зоны открывать под какую группу — решает панель (см.
    /// <see cref="CustomizationPanel"/>), здесь об этом соответствии ничего не знают.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ColorPaletteView : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private static readonly int HueId = Shader.PropertyToID("_Hue");

        [Tooltip("Квадрат насыщенность/яркость: по нему водят ручку, а градиент под " +
                 "текущий оттенок рисует материал шейдера LongLeap/Customization/HSVSquare. " +
                 "Пусто — берётся Image с этого же объекта.")]
        [SerializeField] private Image _paletteImage;

        [Tooltip("Ручка (joystik): её и таскают внутри квадрата палитры. Пусто — " +
                 "перекрас будет работать, но метка выбранного цвета не двигается.")]
        [SerializeField] private RectTransform _handle;

        [Tooltip("Полоска оттенка (colorSlider): задаёт оттенок, под который красится " +
                 "квадрат. Пусто — оттенок всегда красный (0).")]
        [SerializeField] private HueSliderView _hueSlider;

        [Tooltip("Невидимая подложка на весь экран позади палитры: клик по ней (то есть " +
                 "мимо квадрата и полоски) прячет палитру. Включается в Open и гасится в " +
                 "Close — сама палитра не отслеживает клики снаружи себя. Пусто — палитра " +
                 "закрывается только повторным нажатием той же кнопки color.")]
        [SerializeField] private GameObject _clickOutsideBlocker;

        /// <summary>Кому сейчас красим — появляется в <see cref="Open"/>, пропадает в <see cref="Close"/>.</summary>
        private IColorTarget _target;

        /// <summary>Зоны перекраса, которые получают выбранный цвет разом.</summary>
        private IReadOnlyList<string> _zones;

        /// <summary>
        /// Чем открыли палитру — не имя зоны (их может быть несколько), а идентификатор
        /// вызывающей стороны (у панели — имя группы). Нужен ровно для одного: повторное
        /// нажатие той же кнопки color должно закрыть палитру, а не перекрасить заново.
        /// </summary>
        private string _openKey;

        /// <summary>Инстанс материала квадрата — крутим у него _Hue, не трогая общий ассет.</summary>
        private Material _material;

        private float _hue;
        private float _saturation = 1f;
        private float _value = 1f;

        /// <summary>Текущий оттенок квадрата (0..1) — по нему полоска ставит свою ручку при включении.</summary>
        public float Hue => _hue;

        /// <summary>
        /// Цвет применили к зонам — здесь, а не отдельным опросом: у самой палитры нет
        /// иного способа сказать наружу «покрасили вот в это», а панели он нужен, чтобы
        /// подсветить кнопку color группы её же цветом (см. <see cref="CustomizationPanel"/>).
        /// </summary>
        public event Action<Color> ColorChanged;

        /// <summary>Палитра открыта и настроена ровно на этот ключ (без учёта регистра).</summary>
        public bool IsOpenFor(string key) =>
            gameObject.activeSelf && string.Equals(_openKey, key, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Показать палитру и направить дальнейший выбор цвета сразу во все
        /// <paramref name="zones"/> цели <paramref name="target"/>. Открыть можно и под
        /// другой ключ — старые зоны просто перестанут получать цвета.
        /// </summary>
        public void Open(string key, IReadOnlyList<string> zones, IColorTarget target)
        {
            _openKey = key;
            _zones = zones;
            _target = target;

            gameObject.SetActive(true);

            if (_clickOutsideBlocker != null)
                _clickOutsideBlocker.SetActive(true);
        }

        /// <summary>Спрятать палитру. Безопасно звать и когда она уже спрятана.</summary>
        public void Close()
        {
            gameObject.SetActive(false);

            if (_clickOutsideBlocker != null)
                _clickOutsideBlocker.SetActive(false);

            _target = null;
            _zones = null;
            _openKey = null;
        }

        public void OnPointerDown(PointerEventData eventData) => PickAt(eventData);
        public void OnDrag(PointerEventData eventData) => PickAt(eventData);
        public void OnPointerUp(PointerEventData eventData) => PickAt(eventData);

        /// <summary>Оттенок сменила полоска (<see cref="HueSliderView"/>) — перекрасить квадрат и зоны.</summary>
        public void SetHue(float hue)
        {
            _hue = Mathf.Repeat(hue, 1f);

            ApplyHueToMaterial();
            ApplyColor();
        }

        /// <summary>
        /// Переставить ручку под палец/луч (зажатую в границах квадрата) и покрасить
        /// зоны в цвет с текущими насыщенностью/яркостью под ней.
        /// </summary>
        private void PickAt(PointerEventData eventData)
        {
            var image = ResolvePaletteImage();
            if (image == null) return;

            var rect = image.rectTransform;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rect, eventData.position, eventData.pressEventCamera, out var local))
                return;

            var width = rect.rect.width;
            var height = rect.rect.height;
            if (width <= 0f || height <= 0f) return;

            _saturation = Mathf.Clamp01((local.x - rect.rect.x) / width);
            _value = Mathf.Clamp01((local.y - rect.rect.y) / height);

            PlaceHandle(rect);
            ApplyColor();
        }

        private void PlaceHandle(RectTransform rect)
        {
            if (_handle == null) return;

            // Точка считается в системе самого квадрата (от его pivot), а anchoredPosition
            // ручки отсчитывается от её якоря в родителе — при другом pivot/якорях они
            // расходятся и ручка уезжает. Поэтому ставим через мировую позицию: ей всё равно.
            var local = new Vector3(
                rect.rect.x + _saturation * rect.rect.width,
                rect.rect.y + _value * rect.rect.height,
                0f);

            _handle.position = rect.TransformPoint(local);
        }

        private void ApplyColor()
        {
            if (_target == null || _zones == null) return;

            var color = Color.HSVToRGB(_hue, _saturation, _value);

            for (var i = 0; i < _zones.Count; i++)
                _target.SetZoneColor(_zones[i], color);

            ColorChanged?.Invoke(color);
        }

        private void ApplyHueToMaterial()
        {
            if (_material != null)
                _material.SetFloat(HueId, _hue);
        }

        private void Awake()
        {
            var image = ResolvePaletteImage();
            if (image == null || image.material == null) return;

            // Инстанс материала — иначе крутили бы _Hue у общего ассета, и все палитры
            // на экране (если их когда-нибудь будет несколько) дёргались бы друг за другом.
            _material = Instantiate(image.material);
            image.material = _material;

            ApplyHueToMaterial();
        }

        private void OnEnable()
        {
            var image = ResolvePaletteImage();
            if (image != null)
                PlaceHandle(image.rectTransform);
        }

        private Image ResolvePaletteImage()
        {
            if (_paletteImage == null)
                _paletteImage = GetComponent<Image>();

            return _paletteImage;
        }
    }
}
