using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 点地移动落点光标（LoL 式）：点击地面时小圈快速弹出到略大再整体淡出，仅作移动引导。
    /// 纯表现组件，运行时由 PlayerFortress 创建，不写入场景/预制体；
    /// 存活时间、世界尺寸、颜色由 PlayerFortress 的序列化字段传入，方便策划调参。
    /// </summary>
    public class TapMoveMarker : MonoBehaviour
    {
        private SpriteRenderer _sr;
        private float _life;
        private float _size;
        private Color _color;
        private float _t = -1f;
        private float _baseScale = 1f;

        /// <summary>在世界根节点下创建一个平铺地面的光标对象，默认隐藏</summary>
        public static TapMoveMarker Create(Transform parent, float life, float size, Color color)
        {
            var go = new GameObject("TapMoveMarker");
            go.transform.SetParent(parent, false); // 放在世界根下，不跟随战车
            var marker = go.AddComponent<TapMoveMarker>();
            marker._life = life;
            marker._size = size;
            marker._color = color;

            var spr = Resources.Load<Sprite>(VfxKeys.TapMarker);
            if (spr == null) spr = Resources.Load<Sprite>(VfxKeys.RingWave); // 兜底
            if (spr != null)
            {
                marker._sr = go.AddComponent<SpriteRenderer>();
                marker._sr.sprite = spr;
                marker._sr.color = color;
                marker._sr.sortingOrder = 8;
                go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // XZ 地面平铺
                float baseSize = spr.bounds.size.x;
                marker._baseScale = baseSize > 0.0001f ? size / baseSize : 1f;
                go.transform.localScale = Vector3.one * marker._baseScale;
            }
            go.SetActive(false);
            return marker;
        }

        /// <summary>在目标点显示光标并重置弹出动画</summary>
        public void Show(Vector3 p)
        {
            if (_sr == null) return;
            var mt = _sr.transform;
            mt.position = new Vector3(p.x, 0.12f, p.z);
            mt.localScale = Vector3.one * _baseScale * 0.6f;
            _sr.color = _color;
            _sr.gameObject.SetActive(true);
            _t = 0f;
        }

        /// <summary>每帧推进弹出+淡出动画</summary>
        public void Tick(float dt)
        {
            if (_t < 0f || _sr == null) return;
            _t += dt;
            float t = Mathf.Clamp01(_t / _life);
            // 小圈快速弹出到略大，再整体淡出
            float s = Mathf.Lerp(0.6f, 1.08f, 1f - (1f - t) * (1f - t));
            _sr.transform.localScale = Vector3.one * _baseScale * s;
            var c = _color;
            c.a = 1f - t;
            _sr.color = c;
            if (t >= 1f)
            {
                _t = -1f;
                _sr.gameObject.SetActive(false);
            }
        }

        /// <summary>立即隐藏（到点/重开/暂停时）</summary>
        public void Hide()
        {
            _t = -1f;
            if (_sr != null) _sr.gameObject.SetActive(false);
        }
    }
}
