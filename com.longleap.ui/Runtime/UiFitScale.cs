using UnityEngine;
using UnityEngine.UI;

namespace LongLeap.UI
{
    /// <summary>
    /// Уменьшает RectTransform целиком (uniform localScale), если он не помещается в область экрана.
    /// Подходит для панелей с кнопками внутри VerticalLayoutGroup/ContentSizeFitter: лейаут не
    /// трогаем, кнопки не растягиваем — просто масштабируем всю панель.
    ///
    /// Сжимаем так, чтобы точка <see cref="_scaleFrom"/> осталась на месте (pivot панели при этом
    /// может быть где угодно — позиция компенсируется). По умолчанию это верхняя середина панели:
    /// верх не двигается, низ подтягивается вверх. Панель прижата к низу экрана — ставим (0.5, 0),
    /// к левому краю — (0, 0.5) и т.д.: сторона, у которой якорь, сохраняет расстояние до него.
    ///
    /// Масштаб абсолютный: когда панель помещается, он равен <see cref="_maxScale"/> (обычно 1).
    /// Работает только в рантайме: в префаб-моде ничего не меняет и сцену не пачкает.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class UiFitScale : MonoBehaviour
    {
        [Tooltip("Область, в которую вписываем панель. Пусто = корневой Canvas (весь экран).")]
        [SerializeField] private RectTransform _area;

        [Header("Padding (юниты канваса)")]
        [Tooltip("Отступ от левого и нижнего края области.")]
        [SerializeField] private Vector2 _paddingMin = new Vector2(0f, 20f);
        [Tooltip("Отступ от правого и верхнего края области.")]
        [SerializeField] private Vector2 _paddingMax = new Vector2(0f, 0f);
        [Tooltip("Учитывать Screen.safeArea (вырезы, скруглённые углы). Область должна быть на весь экран.")]
        [SerializeField] private bool _useSafeArea = false;

        [Header("Scale")]
        [Tooltip("Точка панели (0..1), вокруг которой масштабируем. (0.5, 1) = верх по центру.")]
        [SerializeField] private Vector2 _scaleFrom = new Vector2(0.5f, 1f);
        [SerializeField] private float _minScale = 0.4f;
        [SerializeField] private float _maxScale = 1f;

        private readonly Vector3[] _corners = new Vector3[4];
        private RectTransform _rect;
        private RectTransform _areaRect;

        private void OnEnable()
        {
            _rect = (RectTransform)transform;
            _areaRect = ResolveArea();

            // Панель обычно включается вместе с окном, а лейаут пересчитывается только в конце кадра.
            // Без принудительной пересборки первый кадр показал бы панель с размером до пересчёта.
            LayoutRebuilder.ForceRebuildLayoutImmediate(_rect);
            Apply();
        }

        private void LateUpdate() => Apply();

        private RectTransform ResolveArea()
        {
            if (_area != null) return _area;

            var canvas = GetComponentInParent<Canvas>();
            return canvas != null ? canvas.rootCanvas.transform as RectTransform : null;
        }

        private void Apply()
        {
            if (_areaRect == null)
            {
                _areaRect = ResolveArea();
                if (_areaRect == null) return;
            }

            // Всё считаем в локальных координатах области: так не зависим от CanvasScaler и режима канваса.
            _rect.GetWorldCorners(_corners);

            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < 4; i++)
            {
                Vector3 c = _areaRect.InverseTransformPoint(_corners[i]);
                min = Vector2.Min(min, c);
                max = Vector2.Max(max, c);
            }

            Rect r = _rect.rect;
            Vector3 pivotWorld = _rect.TransformPoint(new Vector3(
                r.xMin + r.width * _scaleFrom.x,
                r.yMin + r.height * _scaleFrom.y,
                0f));
            Vector2 p = _areaRect.InverseTransformPoint(pivotWorld);

            // Границы при масштабе 1: текущие уже умножены на localScale, откатываем это
            // относительно точки сжатия — результат не зависит от того, какой масштаб стоит сейчас.
            float current = Mathf.Max(Mathf.Abs(_rect.localScale.x), 0.0001f);
            Vector2 lo = p + (min - p) / current;
            Vector2 hi = p + (max - p) / current;

            Rect region = GetRegion();

            float k = Mathf.Min(
                Limit(region.xMin, region.xMax, p.x, lo.x, hi.x),
                Limit(region.yMin, region.yMax, p.y, lo.y, hi.y));
            k = Mathf.Clamp(k, _minScale, Mathf.Max(_minScale, _maxScale));

            if (Mathf.Approximately(current, k)) return;

            // Unity масштабирует вокруг pivot, а не вокруг _scaleFrom: без компенсации край панели
            // уезжал бы от якоря. Точка _scaleFrom лежит на d от pivot (в локальных единицах панели),
            // при смене масштаба с current на k она сместилась бы на d * (k - current) — возвращаем
            // её на место сдвигом позиции. Состояния не храним, поэтому внешняя анимация позиции не мешает.
            Vector2 d = new Vector2(
                (_scaleFrom.x - _rect.pivot.x) * r.width,
                (_scaleFrom.y - _rect.pivot.y) * r.height);
            _rect.anchoredPosition += d * (current - k);
            _rect.localScale = new Vector3(k, k, 1f);
        }

        private Rect GetRegion()
        {
            Rect a = _areaRect.rect;
            float xMin = a.xMin, xMax = a.xMax, yMin = a.yMin, yMax = a.yMax;

            if (_useSafeArea && Screen.width > 0 && Screen.height > 0)
            {
                Rect sa = Screen.safeArea;
                xMin = Mathf.Lerp(a.xMin, a.xMax, sa.xMin / Screen.width);
                xMax = Mathf.Lerp(a.xMin, a.xMax, sa.xMax / Screen.width);
                yMin = Mathf.Lerp(a.yMin, a.yMax, sa.yMin / Screen.height);
                yMax = Mathf.Lerp(a.yMin, a.yMax, sa.yMax / Screen.height);
            }

            return Rect.MinMaxRect(
                xMin + _paddingMin.x,
                yMin + _paddingMin.y,
                xMax - _paddingMax.x,
                yMax - _paddingMax.y);
        }

        /// Максимальный масштаб по одной оси, при котором [lo..hi] (границы при масштабе 1)
        /// остаются внутри [regionMin..regionMax], если сжимать относительно точки p.
        private static float Limit(float regionMin, float regionMax, float p, float lo, float hi)
        {
            float k = float.MaxValue;
            if (hi > p) k = Mathf.Min(k, (regionMax - p) / (hi - p));
            if (lo < p) k = Mathf.Min(k, (p - regionMin) / (p - lo));
            return k;
        }
    }
}
