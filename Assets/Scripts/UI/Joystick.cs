using UnityEngine;
using UnityEngine.EventSystems;

namespace BattleFortress
{
    /// <summary>
    /// 虚拟摇杆。对应 LayaAir 版 ui/Joystick.ts。
    /// 坐标语义沿用 Laya 版：y 向下为正（PlayerFortress 里用 -ay 取前进方向），
    /// 因此在 Unity（y 向上为正）这里对 y 取负后再输出。
    /// </summary>
    public class Joystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [SerializeField] private RectTransform thumb;
        [SerializeField] private RectTransform pad;      // 摇杆底座，用于计算局部坐标
        [SerializeField] private float radius = 78f;

        private bool _dragging;
        private Vector2 _center;

        private void Awake()
        {
            if (pad == null) pad = transform as RectTransform;
        }

        private void OnEnable()
        {
            ResetThumb();
        }

        private void ResetThumb()
        {
            if (thumb != null) thumb.anchoredPosition = Vector2.zero;
            Joy.Reset();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (GS.Paused || GS.Over) return;
            if (!TryLocal(eventData, out var local)) return;
            _dragging = true;
            Joy.active = true;
            Apply(local);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging) return;
            if (!TryLocal(eventData, out var local)) return;
            Apply(local);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _dragging = false;
            ResetThumb();
        }

        private bool TryLocal(PointerEventData eventData, out Vector2 local)
        {
            var rt = pad != null ? pad : (transform as RectTransform);
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rt, eventData.position,
                eventData.pressEventCamera != null ? eventData.pressEventCamera : Camera.main,
                out local);
        }

        private void Apply(Vector2 local)
        {
            float dx = local.x;
            float dy = local.y;
            float dl = Mathf.Sqrt(dx * dx + dy * dy);

            if (dl > radius)
            {
                dx = (dx / dl) * radius;
                dy = (dy / dl) * radius;
            }

            Joy.x = dx / radius;
            Joy.y = -dy / radius;   // Unity y 向上为正 → 转成 Laya 语义（向下为正）

            if (thumb != null) thumb.anchoredPosition = new Vector2(dx, dy);
        }
    }
}
