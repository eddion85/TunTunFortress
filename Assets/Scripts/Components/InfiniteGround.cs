using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 无限跟随地面：运行时铺一圈地砖（(2r+1)² 块），玩家跨过一整块时整体平移一格，
    /// 视觉上永远走不到头。地砖材质自动复制场景里现有地面，找不到就用兜底纯色。
    /// 不依赖物理碰撞（点地移动用的是数学 Plane），纯视觉，不改场景资源。
    /// </summary>
    public class InfiniteGround : MonoBehaviour
    {
        private Transform _follow;
        private Transform[,] _tiles;
        private float _tile;
        private int _r;
        private Vector2Int _origin; // 当前网格中心所在的格子坐标

        /// <summary>由 PlayerFortress 启动时调用；follow=跟随目标（玩家）</summary>
        public void Init(Transform follow)
        {
            _follow = follow;
            _tile = GameConfig.GroundTileSize;
            _r = Mathf.Max(1, GameConfig.GroundTileRadius);

            var mat = BorrowGroundMaterial();
            int side = _r * 2 + 1;
            _tiles = new Transform[side, side];
            for (int x = 0; x < side; x++)
            {
                for (int z = 0; z < side; z++)
                {
                    var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    quad.name = "GroundTile";
                    var col = quad.GetComponent<Collider>();
                    if (col != null) Destroy(col); // 不需要碰撞，避免干扰任何射线
                    quad.transform.SetParent(transform, false);
                    quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // Quad 默认在 XY 面，转成 XZ 地面
                    quad.transform.localScale = new Vector3(_tile, _tile, 1f);
                    var mr = quad.GetComponent<MeshRenderer>();
                    mr.sharedMaterial = mat;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    _tiles[x, z] = quad.transform;
                }
            }
            _origin = new Vector2Int(int.MaxValue, int.MaxValue);
            Lay(true);
        }

        private void Update()
        {
            if (_follow == null || _tiles == null) return;
            Lay(false);
        }

        /// <summary>按玩家所在格子铺设/整体平移地砖</summary>
        private void Lay(bool force)
        {
            Vector3 p = _follow.position;
            var cell = new Vector2Int(
                Mathf.RoundToInt(p.x / _tile),
                Mathf.RoundToInt(p.z / _tile));
            if (!force && cell == _origin) return;
            _origin = cell;

            float cx = cell.x * _tile;
            float cz = cell.y * _tile;
            int side = _r * 2 + 1;
            for (int i = 0; i < side; i++)
            {
                for (int j = 0; j < side; j++)
                {
                    float wx = cx + (i - _r) * _tile;
                    float wz = cz + (j - _r) * _tile;
                    _tiles[i, j].position = new Vector3(wx, GameConfig.GroundY, wz);
                }
            }
        }

        /// <summary>找场景里最平、覆盖面最大的网格（即现有地面），借它的材质；找不到给兜底材质</summary>
        private Material BorrowGroundMaterial()
        {
            MeshRenderer best = null;
            float bestArea = 0f;
            var all = FindObjectsOfType<MeshRenderer>();
            for (int i = 0; i < all.Length; i++)
            {
                var r = all[i];
                var s = r.bounds.size;
                if (s.y > 1f) continue; // 地面是平的，排除立着的物体
                float area = s.x * s.z;
                if (area > bestArea) { bestArea = area; best = r; }
            }
            if (best != null && best.sharedMaterial != null) return best.sharedMaterial;

            // 兜底：哑光草绿色
            var m = new Material(Shader.Find("Standard"));
            m.color = new Color(0.45f, 0.55f, 0.30f);
            return m;
        }
    }
}
