using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LongLeap.Customization
{
    /// <summary>
    /// ScrollRect, который умеет жить внутри другого ScrollRect с перпендикулярной
    /// осью — ровно наш случай: строка группы скроллится по горизонтали
    /// (<see cref="SkinGroupRow"/>), а лежит она внутри <c>Category_Root</c>,
    /// который скроллится по вертикали.
    ///
    /// Обычный ScrollRect перехватывает жест целиком, даже если он вдоль чужой
    /// оси: EventSystem ищет ближайшего предка с IBeginDragHandler и на нём же
    /// останавливается, выше не поднимается. Значит вертикальный свайп по кнопке
    /// внутри строки доставался бы горизонтальному ScrollRect'у строки — и
    /// глох там, а Category_Root его так и не увидел бы.
    ///
    /// Здесь в начале жеста смотрим, куда он в основном направлен (по вектору
    /// от нажатия до текущей точки): вдоль своей оси — скроллим сами, поперёк —
    /// на всё время жеста передоверяем его ближайшему ScrollRect-предку.
    /// Предка нет (это внешний скролл, как у Category_Root) — ведёт себя как
    /// обычный ScrollRect.
    /// </summary>
    [AddComponentMenu("UI/Nested Scroll Rect")]
    public class NestedScrollRect : ScrollRect
    {
        private ScrollRect _parent;
        private bool _routeToParent;

        protected override void Awake()
        {
            base.Awake();

            _parent = FindParentScrollRect();
        }

        private ScrollRect FindParentScrollRect()
        {
            var current = transform.parent;

            while (current != null)
            {
                var candidate = current.GetComponent<ScrollRect>();
                if (candidate != null) return candidate;

                current = current.parent;
            }

            return null;
        }

        /// <summary>Жест в основном вдоль оси, которую скроллит этот ScrollRect.</summary>
        private bool OwnAxisWins(PointerEventData eventData)
        {
            var total = eventData.position - eventData.pressPosition;
            var horizontalDelta = Mathf.Abs(total.x);
            var verticalDelta = Mathf.Abs(total.y);

            return horizontal ? horizontalDelta >= verticalDelta : verticalDelta > horizontalDelta;
        }

        public override void OnBeginDrag(PointerEventData eventData)
        {
            _routeToParent = _parent != null && !OwnAxisWins(eventData);

            if (_routeToParent)
                _parent.OnBeginDrag(eventData);
            else
                base.OnBeginDrag(eventData);
        }

        public override void OnDrag(PointerEventData eventData)
        {
            if (_routeToParent)
                _parent.OnDrag(eventData);
            else
                base.OnDrag(eventData);
        }

        public override void OnEndDrag(PointerEventData eventData)
        {
            if (_routeToParent)
                _parent.OnEndDrag(eventData);
            else
                base.OnEndDrag(eventData);

            _routeToParent = false;
        }
    }
}
