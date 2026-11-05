using UnityEngine;
using UnityEngine.UI;

namespace LongLeap.UI
{
    /// <summary>
    /// Shrinks the whole RectTransform (uniform localScale) if it doesn't fit into the screen area.
    /// Suited for panels with buttons inside a VerticalLayoutGroup/ContentSizeFitter: the layout is
    /// left alone, buttons are not stretched — the whole panel is simply scaled.
    ///
    /// Shrinks so that the <see cref="_scaleFrom"/> point stays in place (the panel pivot itself
    /// can be anywhere — position is compensated). By default it is the panel's top center:
    /// the top stays put, the bottom moves up. Panel pinned to the bottom of the screen — use (0.5, 0),
    /// to the left edge — (0, 0.5), etc.: the side with the anchor keeps its distance to it.
    ///
    /// The scale is absolute: when the panel fits, it equals <see cref="_maxScale"/> (usually 1).
    /// Works only at runtime: changes nothing in prefab mode and doesn't dirty the scene.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class UiFitScale : MonoBehaviour
    {
        [Tooltip("Area the panel is fitted into. Empty = root Canvas (the whole screen).")]
        [SerializeField] private RectTransform _area;

        [Header("Padding (canvas units)")]
        [Tooltip("Offset from the left and bottom edges of the area.")]
        [SerializeField] private Vector2 _paddingMin = new Vector2(0f, 20f);
        [Tooltip("Offset from the right and top edges of the area.")]
        [SerializeField] private Vector2 _paddingMax = new Vector2(0f, 0f);
        [Tooltip("Respect Screen.safeArea (notches, rounded corners). The area must cover the whole screen.")]
        [SerializeField] private bool _useSafeArea = false;

        [Header("Scale")]
        [Tooltip("Panel point (0..1) to scale around. (0.5, 1) = top center.")]
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

            // The panel is usually enabled together with the window, and layout is only recalculated at the end of the frame.
            // Without a forced rebuild, the first frame would show the panel at its pre-recalculation size.
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

            // Everything is computed in the area's local coordinates: this way we don't depend on CanvasScaler or canvas mode.
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

            // Bounds at scale 1: the current ones are already multiplied by localScale, so undo that
            // relative to the scaling point — the result doesn't depend on the scale currently set.
            float current = Mathf.Max(Mathf.Abs(_rect.localScale.x), 0.0001f);
            Vector2 lo = p + (min - p) / current;
            Vector2 hi = p + (max - p) / current;

            Rect region = GetRegion();

            float k = Mathf.Min(
                Limit(region.xMin, region.xMax, p.x, lo.x, hi.x),
                Limit(region.yMin, region.yMax, p.y, lo.y, hi.y));
            k = Mathf.Clamp(k, _minScale, Mathf.Max(_minScale, _maxScale));

            if (Mathf.Approximately(current, k)) return;

            // Unity scales around the pivot, not around _scaleFrom: without compensation the panel edge
            // would drift away from the anchor. The _scaleFrom point lies at d from the pivot (in panel local units);
            // changing the scale from current to k would shift it by d * (k - current) — we move it
            // back by offsetting the position. No state is stored, so external position animation isn't affected.
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

        /// Maximum scale along one axis at which [lo..hi] (bounds at scale 1)
        /// stay inside [regionMin..regionMax] when shrinking relative to point p.
        private static float Limit(float regionMin, float regionMax, float p, float lo, float hi)
        {
            float k = float.MaxValue;
            if (hi > p) k = Mathf.Min(k, (regionMax - p) / (hi - p));
            if (lo < p) k = Mathf.Min(k, (p - regionMin) / (p - lo));
            return k;
        }
    }
}
