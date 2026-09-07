using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace BattleFortress
{
    /// <summary>
    /// 虚拟摇杆。对应 LayaAir 版 ui/Joystick.ts。
    /// 坐标语义沿用 Laya 版：y 向下为正（PlayerFortress 里用 -ay 取前进方向），
    /// 因此在 Unity（y 向上为正）这里对 y 取负后再输出。
    /// 输出为 360° 连续方向（无方向刻度/无八方向量化）。
    /// </summary>
    public class Joystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [SerializeField] private RectTransform thumb;
        [SerializeField] private RectTransform pad;      // 摇杆底座，用于计算局部坐标
        [SerializeField] private float radius = 78f;
        [SerializeField] private float deadZone = 0.04f;  // 起始死区（归一化半径），越小越跟手
        [SerializeField] private float touchSize = 300f;  // 实际可触摸区域边长（只扩射线区域，视觉底盘不变）

        private bool _dragging;

        private void Awake()
        {
            if (pad == null) pad = transform as RectTransform;
            ExpandHitArea();
        }

        /// <summary>
        /// 用 Image.raycastPadding 把底盘的命中区域向外扩到 touchSize，
        /// 不新增任何 GameObject、不改变视觉；手指按在底盘附近也能触发拖拽。
        /// </summary>
        private void ExpandHitArea()
        {
            if (pad == null) return;
            var img = pad.GetComponent<Image>();
            if (img == null) return;
            float w = pad.rect.width > 1f ? pad.rect.width : 150f;
            float e = Mathf.Max(0f, (touchSize - w) * 0.5f);
            // raycastPadding 顺序：left, bottom, right, top；负值=向外扩展
            img.raycastPadding = new Vector4(-e, -e, -e, -e);
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

            // 圆心死区：范围内视为无输入，消除抖动；不限制任何方向
            float mag = radius > 0.001f ? Mathf.Min(dl, radius) / radius : 0f;
            if (mag <= deadZone || dl < 0.0001f)
            {
                Joy.x = 0f;
                Joy.y = 0f;
                if (thumb != null) thumb.anchoredPosition = Vector2.zero;
                return;
            }

            float ux = dx / dl;
            float uy = dy / dl;
            float travel = mag * radius;

            // 越过死区后线性映射到 0..1，方向为连续任意角度
            float k = Mathf.Clamp01((mag - deadZone) / (1f - deadZone));
            Joy.x = ux * k;
            Joy.y = -uy * k;  // Unity y 向上为正 → 转成 Laya 语义（向下为正）

            if (thumb != null) thumb.anchoredPosition = new Vector2(ux * travel, uy * travel);
        }
    }
}
