using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BattleFortress
{
    /// <summary>
    /// 小地图：显示玩家位置与周围敌人分布。
    /// 有边界（ArenaBounded）时按整张竞技场映射；无边界时以玩家为中心开动态窗口，
    /// 玩家恒在中心、半径 MiniMapViewRange 米映射到半张图。
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
        /// 世界坐标 → 小地图像素（相对底板左下角）。cx/cz 为动态窗口中心（玩家坐标）。
        /// 相机位于玩家 -Z 侧、俯看 +Z，yaw=0 时屏幕右方对应世界 +X、屏幕上方对应 +Z，X 不镜像。
        /// </summary>
        private Vector2 ToMap(float x, float z, float cx, float cz)
        {
            float u, v;
            if (GameConfig.ArenaBounded)
            {
                float half = GameConfig.ArenaHalf;
                u = (x + half) / (half * 2f);
                v = (z + half) / (half * 2f);
            }
            else
            {
                float r = GameConfig.MiniMapViewRange;
                u = 0.5f + (x - cx) / (r * 2f);
                v = 0.5f + (z - cz) / (r * 2f);
            }
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
            PlacePlayerMarker(ToMap(pp.x, pp.z, pp.x, pp.z)); // 玩家：动态窗口下恒为中心
            RefreshCoord(pp);
            int used = RefreshEnemyDots(pp);

            for (int i = used; i < _dots.Count; i++)
                if (_dots[i] != null) _dots[i].gameObject.SetActive(false);
        }

        /// <summary>把玩家标记放到对应像素点</summary>
        private void PlacePlayerMarker(Vector2 pm)
        {
            if (marker == null) return;
            var rt = marker.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pm;
        }

        /// <summary>坐标文本：有边界时贴边改提示语与警示色，无边界只显示坐标</summary>
        private void RefreshCoord(Vector3 pp)
        {
            if (coordLabel == null) return;
            var normal = new Color(1f, 0.914f, 0.659f);
            if (GameConfig.ArenaBounded)
            {
                float half = GameConfig.ArenaHalf;
                bool edge = Mathf.Abs(pp.x) > half - 1.2f || Mathf.Abs(pp.z) > half - 1.2f;
                coordLabel.text = edge ? "已到地图边缘" : Mathf.RoundToInt(pp.x) + " , " + Mathf.RoundToInt(pp.z);
                coordLabel.color = edge ? new Color(1f, 0.69f, 0.63f) : normal;
            }
            else
            {
                coordLabel.text = Mathf.RoundToInt(pp.x) + " , " + Mathf.RoundToInt(pp.z);
                coordLabel.color = normal;
            }
        }

        /// <summary>敌人点位：Boss 大红点、有害小怪橙点、无害小怪绿点；窗口外不画。返回本次用掉的点数</summary>
        private int RefreshEnemyDots(Vector3 pp)
        {
            int n = 0;
            for (int i = 0; i < Registry.Enemies.Count && n < maxDots; i++)
            {
                var e = Registry.Enemies[i];
                if (e == null || e.Dead || !e.gameObject.activeInHierarchy) continue;

                var ep = e.transform.position;
                Vector2 m = ToMap(ep.x, ep.z, pp.x, pp.z);
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
            return n;
        }
    }
}
