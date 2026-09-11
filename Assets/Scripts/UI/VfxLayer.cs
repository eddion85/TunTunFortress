using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BattleFortress
{
    /// <summary>
    /// 特效叠加层：把 3D 世界坐标投影到 2D 屏幕。
    /// 职责按 partial 分文件，便于维护：
    ///   VfxLayer.Fx.cs       单图/序列帧特效播放与对象池
    ///   VfxLayer.DmgText.cs  世界空间伤害数字
    ///   VfxLayer.Bars.cs     敌人血条
    ///   VfxLayer.Ground.cs   常驻吞噬圈、地面阴影
    ///   本文件               序列化字段、生命周期、坐标投影、主循环、受击红屏
    /// 对应 LayaAir 版 ui/VfxLayer.ts。
    /// </summary>
    public partial class VfxLayer : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private Camera cam;
        [SerializeField] private RectTransform container;     // 特效/血条挂载容器
        [SerializeField] private Image hurtVignette;
        [SerializeField] private Transform playerNode;
        [SerializeField] private Canvas rootCanvas;

        [Header("容量上限（移动端收紧，防止堆成一片糊）")]
        [SerializeField] private int maxFx = 14;
        [SerializeField] private int maxTrail = 12;   // 移动冒烟拖尾独立上限，不挤占战斗特效
        [SerializeField] private int maxDmg = 12;
        [SerializeField] private int maxBar = 24;
        [SerializeField] private int maxShadow = 22;

        [Header("颜色")]
        [SerializeField] private Color barBgColor = new Color(0.102f, 0.059f, 0.024f);
        [SerializeField] private Color barFillColor = new Color(0.890f, 0.192f, 0.153f);

        [Header("加色发光特效（黑底光效走 Additive，消除黑框）")]
        [Range(0.2f, 1.6f)][SerializeField] private float additiveBoost = 0.95f;

        /// <summary>
        /// 黑底发光类特效：这些贴图是「黑底 + 光形」，必须用加色混合（黑=不发光）。
        /// 烟雾/阴影/落点圈/吞噬圈等靠普通 alpha 混合的特效不在此列。
        /// </summary>
        private static readonly HashSet<string> AdditiveUrls = new HashSet<string>
        {
            VfxKeys.FireBall,
            VfxKeys.HitSheet,
            VfxKeys.ExplosionSheet,
            VfxKeys.EvolveSheet,
            VfxKeys.RingWave,
            VfxKeys.LightBeam,
            VfxKeys.StarSpark,
            VfxKeys.SpeedLine,
            VfxKeys.SoftCircle
        };
        private static Material _additiveMat;

        // ---------------- 内部数据 ----------------
        private class FxItem
        {
            public Image img;
            public Vector3 world;
            public float life, maxLife;
            public Sprite[] frames;
            public float scale;
            public float spin;
            public float rise;
            public float growEnd;     // 末态扩散倍率：尺寸从 scale 涨到 scale*(1+growEnd)
            public float startAlpha;  // 起始透明度，随后线性淡出
            public int frame = -1;
            public Color tint = Color.white; // 染色（地面车辙用土黄）
            public float angle;              // 固定屏幕朝向（度）
            public float stretch = 1f;       // 沿朝向拉长倍数
        }

        private class Bar
        {
            public GameObject root;
            public Image fill;
        }

        private class DmgText
        {
            public Text lb;
            public Vector3 world;
            public float life, max;
            public float dx;
            public bool big;
        }

        private readonly List<FxItem> _items = new List<FxItem>();
        private readonly List<Image> _fxPool = new List<Image>();
        private readonly List<FxItem> _trailItems = new List<FxItem>();
        private readonly List<Image> _trailPool = new List<Image>();
        private readonly List<FxItem> _trackItems = new List<FxItem>(); // 地面车辙印独立池
        private readonly List<Image> _trackPool = new List<Image>();
        private const int MaxTrack = 40;
        private readonly List<Bar> _bars = new List<Bar>();
        private readonly List<Image> _shadows = new List<Image>();
        private readonly List<DmgText> _dmgs = new List<DmgText>();
        private readonly List<Text> _dmgPool = new List<Text>();

        private readonly Dictionary<string, Sprite[]> _frameCache = new Dictionary<string, Sprite[]>();

        private Image _ring;
        private float _ringT;
        private float _hurt;

        // ---------------- 生命周期 ----------------
        private void OnEnable()
        {
            GameBus.On(GameEvents.Vfx, OnVfx);
            GameBus.On(GameEvents.Restart, ClearAll);
            GameBus.On(GameEvents.Hurt, OnHurt);
            GameBus.On(GameEvents.Dmg, OnDmg);
            if (hurtVignette != null) hurtVignette.color = new Color(1f, 1f, 1f, 0f);
        }

        private void OnDisable()
        {
            GameBus.Off(GameEvents.Vfx, OnVfx);
            GameBus.Off(GameEvents.Restart, ClearAll);
            GameBus.Off(GameEvents.Hurt, OnHurt);
            GameBus.Off(GameEvents.Dmg, OnDmg);
        }

        private void Start()
        {
            if (cam == null) cam = Camera.main;
            if (container == null) container = transform as RectTransform;
        }

        private Camera UiCamera()
        {
            if (rootCanvas == null) return null;
            return rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;
        }

        /// <summary>世界坐标 → 容器局部坐标；在相机背后返回 false</summary>
        private bool Project(Vector3 world, out Vector2 local)
        {
            local = Vector2.zero;
            if (cam == null || container == null) return false;
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) return false;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(container, sp, UiCamera(), out local);
        }

        private void ClearAll(object payload)
        {
            for (int i = 0; i < _items.Count; i++)
                if (_items[i] != null && _items[i].img != null) RecycleFx(_items[i].img, _fxPool);
            _items.Clear();

            for (int i = 0; i < _trailItems.Count; i++)
                if (_trailItems[i] != null && _trailItems[i].img != null) RecycleFx(_trailItems[i].img, _trailPool);
            _trailItems.Clear();

            for (int i = 0; i < _trackItems.Count; i++)
                if (_trackItems[i] != null && _trackItems[i].img != null) RecycleFx(_trackItems[i].img, _trackPool);
            _trackItems.Clear();

            for (int i = 0; i < _bars.Count; i++)
                if (_bars[i] != null && _bars[i].root != null) _bars[i].root.SetActive(false);
            _hurt = 0f;
            if (hurtVignette != null) hurtVignette.color = new Color(1f, 1f, 1f, 0f);
        }

        private void OnHurt(object payload) { _hurt = 0.45f; }

        // ---------------- 主循环 ----------------
        private void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, GameConfig.MaxDelta);
            if (cam == null || container == null) return;

            // 受击红屏渐隐
            if (hurtVignette != null)
            {
                if (_hurt > 0f)
                {
                    _hurt -= dt;
                    var c = hurtVignette.color;
                    c.a = Mathf.Max(0f, _hurt / 0.45f) * 0.65f;
                    hurtVignette.color = c;
                }
                else if (hurtVignette.color.a != 0f)
                {
                    var c = hurtVignette.color;
                    c.a = 0f;
                    hurtVignette.color = c;
                }
            }

            if (!GS.Paused && !GS.Over)
            {
                UpdateDevourRing(dt);
                UpdateShadows();
                UpdateBars();
            }

            UpdateDmgs(dt);

            // 特效推进（战斗特效 + 移动拖尾两条独立列表）
            StepFxList(_items, _fxPool, dt);
            StepFxList(_trailItems, _trailPool, dt);
            StepFxList(_trackItems, _trackPool, dt);
        }
    }
}
