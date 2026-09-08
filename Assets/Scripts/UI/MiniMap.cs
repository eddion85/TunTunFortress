using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BattleFortress
{
    /// <summary>
    /// 小地图：显示玩家在竞技场中的位置与周围敌人分布。
    /// 同时解决「走到边界以为卡住」的问题——边框即为可活动范围。
    /// 对应 LayaAir 版 ui/MiniMap.ts。
    /// </summary>
    public class MiniMap : MonoBehaviour
    {
        [SerializeField] private RectTransform board;   // 地图底板，尺寸即绘制范围
        [SerializeField] private Transform playerNode;
        [SerializeField] private Image marker;          // 玩家标记
        [SerializeField] private Text coordLabel;
        [SerializeField] private int maxDots = 30;

        private readonly List<Image> _dots = new List<Image>();
        private float _size = 150f;
        private static Sprite _dotSprite;

        private void Start()
        {
            if (board != null) _size = board.rect.width > 0 ? board.rect.width : 150f;
            GameBus.On(GameEvents.Restart, OnRestart);
        }

        private void OnDestroy()
        {
            GameBus.Off(GameEvents.Restart, OnRestart);
        }

        private void OnRestart(object payload)
        {
            for (int i = 0; i < _dots.Count; i++)
                if (_dots[i] != null) _dots[i].gameObject.SetActive(false);
        }

        /// <summary>运行时生成一个纯白圆形 sprite，用 color 染色复用</summary>
        private static Sprite DotSprite()
        {
            if (_dotSprite != null) return _dotSprite;
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - c;
                    float dy = y - c;
                    bool inside = dx * dx + dy * dy <= c * c;
                    tex.SetPixel(x, y, inside ? Color.white : Color.clear);
                }
            }
            tex.Apply();
            _dotSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
            return _dotSprite;
        }

        /// <summary>
        /// 世界坐标 → 小地图像素（相对底板左下角）。
        /// 相机位于玩家 -Z 侧、俯看 +Z，yaw=0 时屏幕右方对应世界 +X、屏幕上方对应 +Z，
        /// 因此小地图 X 不镜像（旧实现错误镜像导致左右与实际移动相反）。
        /// </summary>
        private Vector2 ToMap(float x, float z)
        {
            float half = GameConfig.ArenaHalf;
            float u = (x + half) / (half * 2f);
            float v = (z + half) / (half * 2f);
            return new Vector2(u * _size, v * _size);
        }

        private Image ObtainDot(int index)
        {
            if (index < _dots.Count && _dots[index] != null) return _dots[index];

            var go = new GameObject("Dot", typeof(RectTransform));
            go.transform.SetParent(board != null ? board : (transform as RectTransform), false);
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            img.sprite = DotSprite();

            var rt = go.transform as RectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(8f, 8f);

            if (index < _dots.Count) _dots[index] = img;
            else _dots.Add(img);
            return img;
        }

        private void Update()
        {
            if (playerNode == null || board == null) return;
            if (GS.Paused || GS.Over) return;

            Vector3 pp = playerNode.position;
            Vector2 pm = ToMap(pp.x, pp.z);

            if (marker != null)
            {
                var rt = marker.rectTransform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.zero;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = pm;
            }

            if (coordLabel != null)
            {
                // 贴边时提示玩家已到边界，而不是让人以为卡住了
                float half = GameConfig.ArenaHalf;
                bool edge = Mathf.Abs(pp.x) > half - 1.2f || Mathf.Abs(pp.z) > half - 1.2f;
                coordLabel.text = edge ? "已到地图边缘" : Mathf.RoundToInt(pp.x) + " , " + Mathf.RoundToInt(pp.z);
                coordLabel.color = edge ? new Color(1f, 0.69f, 0.63f) : new Color(1f, 0.914f, 0.659f);
            }

            // 敌人点位：Boss 用大红点，普通敌人小点
            int n = 0;
            for (int i = 0; i < Registry.Enemies.Count && n < maxDots; i++)
            {
                var e = Registry.Enemies[i];
                if (e == null || e.Dead || !e.gameObject.activeInHierarchy) continue;

                var ep = e.transform.position;
                Vector2 m = ToMap(ep.x, ep.z);
                if (m.x < 0f || m.x > _size || m.y < 0f || m.y > _size) continue;

                var dot = ObtainDot(n++);
                dot.gameObject.SetActive(true);
                dot.rectTransform.anchoredPosition = m;
                if (e.IsBoss)
                {
                    dot.rectTransform.sizeDelta = new Vector2(14f, 14f);
                    dot.color = new Color(1f, 0.29f, 0.18f);       // #ff4a2e
                }
                else
                {
                    dot.rectTransform.sizeDelta = new Vector2(8f, 8f);
                    dot.color = e.Damage() > 0f
                        ? new Color(1f, 0.604f, 0.353f)            // #ff9a5a 有害
                        : new Color(0.847f, 0.910f, 0.627f);       // #d8e8a0 无害
                }
            }

            for (int i = n; i < _dots.Count; i++)
                if (_dots[i] != null) _dots[i].gameObject.SetActive(false);
        }
    }
}
