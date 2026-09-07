using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BattleFortress
{
    /// <summary>
    /// 特效叠加层：把 3D 世界坐标投影到 2D 屏幕。
    /// 负责：单图/序列帧特效播放、敌人血条、世界空间伤害数字、常驻吞噬圈、地面阴影、受击红屏。
    /// 对应 LayaAir 版 ui/VfxLayer.ts。
    /// </summary>
    public class VfxLayer : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private Camera cam;
        [SerializeField] private RectTransform container;     // 特效/血条挂载容器
        [SerializeField] private Image hurtVignette;
        [SerializeField] private Transform playerNode;
        [SerializeField] private Canvas rootCanvas;

        [Header("容量上限（移动端收紧，防止堆成一片糊）")]
        [SerializeField] private int maxFx = 14;
        [SerializeField] private int maxDmg = 12;
        [SerializeField] private int maxBar = 24;
        [SerializeField] private int maxShadow = 22;

        [Header("颜色")]
        [SerializeField] private Color barBgColor = new Color(0.102f, 0.059f, 0.024f);
        [SerializeField] private Color barFillColor = new Color(0.890f, 0.192f, 0.153f);

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
            public int frame = -1;
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
        private readonly List<Bar> _bars = new List<Bar>();
        private readonly List<Image> _shadows = new List<Image>();
        private readonly List<DmgText> _dmgs = new List<DmgText>();
        private readonly List<Text> _dmgPool = new List<Text>();

        private readonly Dictionary<string, Sprite[]> _frameCache = new Dictionary<string, Sprite[]>();

        private Image _ring;
        private float _ringT;
        private float _hurt;
        private static Font _font;

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

        private static Font DefaultFont()
        {
            if (_font != null) return _font;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return _font;
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
                if (_items[i] != null && _items[i].img != null) RecycleFx(_items[i].img);
            _items.Clear();

            for (int i = 0; i < _bars.Count; i++)
                if (_bars[i] != null && _bars[i].root != null) _bars[i].root.SetActive(false);
            _hurt = 0f;
            if (hurtVignette != null) hurtVignette.color = new Color(1f, 1f, 1f, 0f);
        }

        private void OnHurt(object payload) { _hurt = 0.45f; }

        // ---------------- 特效 ----------------
        private Sprite[] LoadFrames(string url)
        {
            Sprite[] arr;
            if (_frameCache.TryGetValue(url, out arr)) return arr;
            arr = Resources.LoadAll<Sprite>(url);
            if (arr == null || arr.Length == 0)
            {
                // 没切成子 sprite 时，退回整图
                var single = Resources.Load<Sprite>(url);
                arr = single != null ? new[] { single } : new Sprite[0];
            }
            _frameCache[url] = arr;
            return arr;
        }

        private Image ObtainFx()
        {
            Image img;
            if (_fxPool.Count > 0)
            {
                img = _fxPool[_fxPool.Count - 1];
                _fxPool.RemoveAt(_fxPool.Count - 1);
            }
            else
            {
                var go = new GameObject("Fx", typeof(RectTransform));
                go.transform.SetParent(container, false);
                img = go.AddComponent<Image>();
                img.raycastTarget = false;
                img.preserveAspect = true;
            }
            img.gameObject.SetActive(true);
            img.color = Color.white;
            img.transform.localRotation = Quaternion.identity;
            img.rectTransform.anchoredPosition = Vector2.zero;
            img.rectTransform.localScale = Vector3.one;
            return img;
        }

        private void RecycleFx(Image img)
        {
            if (img == null) return;
            img.gameObject.SetActive(false);
            if (_fxPool.Count < 24) _fxPool.Add(img);
            else Destroy(img.gameObject);
        }

        private void OnVfx(object payload)
        {
            var p = payload as VfxPayload;
            if (p == null || container == null || _items.Count >= maxFx) return;

            var frames = LoadFrames(p.url);
            if (frames.Length == 0) return;

            var img = ObtainFx();
            img.sprite = frames[0];

            float life = frames.Length > 1 ? 0.28f : 0.34f;
            _items.Add(new FxItem
            {
                img = img,
                world = new Vector3(p.x, p.y, p.z),
                life = life,
                maxLife = life,
                frames = frames,
                scale = p.scale,
                spin = frames.Length > 1 ? 0f : Random.value * 40f - 20f,
                rise = frames.Length > 1 ? 10f : 26f,
                frame = 0
            });
        }

        // ---------------- 伤害数字 ----------------
        private void OnDmg(object payload)
        {
            var p = payload as DmgPayload;
            if (p == null || container == null || _dmgs.Count >= maxDmg) return;

            Text lb;
            if (_dmgPool.Count > 0)
            {
                lb = _dmgPool[_dmgPool.Count - 1];
                _dmgPool.RemoveAt(_dmgPool.Count - 1);
            }
            else
            {
                var go = new GameObject("Dmg", typeof(RectTransform));
                go.transform.SetParent(container, false);
                lb = go.AddComponent<Text>();
                lb.raycastTarget = false;
                lb.font = DefaultFont();
            }

            lb.gameObject.SetActive(true);
            lb.text = p.text;
            lb.fontSize = p.big ? 46 : 34;
            lb.fontStyle = FontStyle.Bold;
            lb.color = new Color(1f, 0.824f, 0.290f);      // #ffd24a
            lb.alignment = TextAnchor.MiddleCenter;
            lb.horizontalOverflow = HorizontalWrapMode.Overflow;
            lb.verticalOverflow = VerticalWrapMode.Overflow;
            var rt = lb.rectTransform;
            rt.sizeDelta = new Vector2(160f, 60f);
            rt.anchoredPosition = Vector2.zero;
            lb.color = p.big ? new Color(1f, 0.824f, 0.290f) : new Color(1f, 0.953f, 0.769f);

            _dmgs.Add(new DmgText
            {
                lb = lb,
                world = new Vector3(p.x, p.y, p.z),
                life = 0.6f,
                max = 0.6f,
                dx = Random.value * 46f - 23f,
                big = p.big
            });
        }

        private void UpdateDmgs(float dt)
        {
            for (int i = _dmgs.Count - 1; i >= 0; i--)
            {
                var d = _dmgs[i];
                d.life -= dt;
                if (d.life <= 0f || d.lb == null)
                {
                    if (d.lb != null)
                    {
                        d.lb.gameObject.SetActive(false);
                        if (_dmgPool.Count < 24) _dmgPool.Add(d.lb);
                        else Destroy(d.lb.gameObject);
                    }
                    _dmgs.RemoveAt(i);
                    continue;
                }

                float t = 1f - d.life / d.max;
                Vector2 local;
                if (!Project(d.world, out local))
                {
                    d.lb.gameObject.SetActive(false);
                    continue;
                }
                d.lb.gameObject.SetActive(true);
                d.lb.rectTransform.anchoredPosition = new Vector2(local.x + d.dx * t, local.y - 90f * t);
                d.lb.color = new Color(d.lb.color.r, d.lb.color.g, d.lb.color.b, t < 0.65f ? 1f : (1f - t) / 0.35f);

                // 弹出缩放：先放大再收回
                float baseSize = d.big ? 1.4f : 1.15f;
                float k = t < 0.18f ? baseSize * (1f + (0.18f - t) * 2.2f) : baseSize;
                d.lb.rectTransform.localScale = new Vector3(k, k, 1f);
            }
        }

        // ---------------- 血条 ----------------
        private Bar ObtainBar()
        {
            for (int i = 0; i < _bars.Count; i++)
                if (_bars[i] != null && _bars[i].root != null && !_bars[i].root.activeSelf) return _bars[i];

            var go = new GameObject("Bar", typeof(RectTransform));
            go.transform.SetParent(container, false);
            var bg = go.AddComponent<Image>();
            bg.color = barBgColor;
            bg.raycastTarget = false;

            var fillGo = new GameObject("Fill", typeof(RectTransform));
            fillGo.transform.SetParent(go.transform, false);
            var fill = fillGo.AddComponent<Image>();
            fill.color = barFillColor;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.raycastTarget = false;

            var frt = fill.rectTransform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = new Vector2(1.5f, 1.5f);
            frt.offsetMax = new Vector2(-1.5f, -1.5f);

            var bar = new Bar { root = go, fill = fill };
            _bars.Add(bar);
            return bar;
        }

        private void UpdateBars()
        {
            int used = 0;
            for (int i = 0; i < Registry.Enemies.Count && used < maxBar; i++)
            {
                var e = Registry.Enemies[i];
                if (e == null || e.Dead || !e.gameObject.activeInHierarchy) continue;

                var bar = ObtainBar();
                Vector3 p = e.transform.position;
                float h = e.IsBoss ? 3.4f : 1.8f;
                Vector2 local;
                if (!Project(new Vector3(p.x, p.y + h, p.z), out local))
                {
                    bar.root.SetActive(false);
                    continue;
                }

                bar.root.SetActive(true);
                float w = e.IsBoss ? 220f : 76f;
                float bh = e.IsBoss ? 20f : 11f;
                float ratio = Mathf.Clamp01(e.Hp / Mathf.Max(1f, e.MaxHp));

                var rt = bar.root.transform as RectTransform;
                rt.sizeDelta = new Vector2(w, bh);
                rt.anchoredPosition = new Vector2(local.x, local.y);
                bar.fill.fillAmount = ratio;
                used++;
            }

            // 回收未用到的血条
            int active = 0;
            for (int i = 0; i < _bars.Count; i++)
            {
                if (_bars[i] == null || _bars[i].root == null) continue;
                if (_bars[i].root.activeSelf)
                {
                    active++;
                    if (active > used) _bars[i].root.SetActive(false);
                }
            }
        }

        // ---------------- 吞噬圈 ----------------
        /// <summary>
        /// 常驻吞噬圈：把 3D 世界半径投影成 2D 圆环贴在玩家脚下，
        /// 让玩家随时看清能吞掉多大范围，并随进化/词条实时变化。
        /// </summary>
        private void UpdateDevourRing(float dt)
        {
            if (playerNode == null || container == null) return;

            if (_ring == null)
            {
                var go = new GameObject("DevourRing", typeof(RectTransform));
                go.transform.SetParent(container, false);
                go.transform.SetAsFirstSibling();
                _ring = go.AddComponent<Image>();
                _ring.raycastTarget = false;
                var sp = Resources.Load<Sprite>(VfxKeys.DevourRing);
                if (sp != null) _ring.sprite = sp;
            }

            Vector3 pp = playerNode.position;
            float r = GS.DevourRadius();

            Vector2 cLocal;
            if (!Project(new Vector3(pp.x, 0.05f, pp.z), out cLocal))
            {
                _ring.gameObject.SetActive(false);
                return;
            }
            Vector2 eLocal;
            if (!Project(new Vector3(pp.x + r, 0.05f, pp.z), out eLocal))
            {
                _ring.gameObject.SetActive(false);
                return;
            }

            float px = Mathf.Max(24f, Mathf.Abs(eLocal.x - cLocal.x));

            // 呼吸脉冲，提示这是活跃的吞噬范围
            _ringT += dt;
            float pulse = 1f + Mathf.Sin(_ringT * 3.2f) * 0.05f;

            _ring.gameObject.SetActive(true);
            _ring.rectTransform.sizeDelta = new Vector2(px * 2f * pulse, px * 2f * pulse * 0.55f);
            _ring.rectTransform.anchoredPosition = cLocal;

            // 基础状态柔和显示；强化期间更亮，末段快闪预警
            if (GS.DevourBoosted())
            {
                float left = GS.DevourTimer;
                float blink = left < 1.5f ? 0.45f + Mathf.Abs(Mathf.Sin(_ringT * 14f)) * 0.5f : 0.85f;
                _ring.color = new Color(1f, 0.824f, 0.290f, blink);
            }
            else
            {
                _ring.color = new Color(1f, 1f, 1f, 0.4f + Mathf.Sin(_ringT * 3.2f) * 0.08f);
            }
        }

        // ---------------- 地面阴影 ----------------
        private void UpdateShadows()
        {
            if (container == null) return;
            int n = 0;

            if (playerNode != null)
            {
                UpdateShadow(n++, playerNode.position, 1.5f);
            }
            for (int i = 0; i < Registry.Enemies.Count && n < maxShadow; i++)
            {
                var e = Registry.Enemies[i];
                if (e == null || e.Dead || !e.gameObject.activeInHierarchy) continue;
                float s = e.IsBoss ? 2.2f : 0.55f + (e.Kind != null ? e.Kind.size : 1) * 0.25f;
                UpdateShadow(n++, e.transform.position, s);
            }

            for (int i = n; i < _shadows.Count; i++)
                if (_shadows[i] != null) _shadows[i].gameObject.SetActive(false);
        }

        private void UpdateShadow(int index, Vector3 world, float sizeScale)
        {
            Image sp;
            if (index < _shadows.Count && _shadows[index] != null)
            {
                sp = _shadows[index];
            }
            else
            {
                var go = new GameObject("Shadow", typeof(RectTransform));
                go.transform.SetParent(container, false);
                go.transform.SetAsFirstSibling();
                sp = go.AddComponent<Image>();
                sp.raycastTarget = false;
                var spr = Resources.Load<Sprite>(VfxKeys.ShadowBlob);
                if (spr != null) sp.sprite = spr;
                sp.color = new Color(1f, 1f, 1f, 0.42f);
                if (index < _shadows.Count) _shadows[index] = sp;
                else _shadows.Add(sp);
            }

            Vector2 local;
            if (!Project(new Vector3(world.x, 0.02f, world.z), out local))
            {
                sp.gameObject.SetActive(false);
                return;
            }

            // 距离越远投影越小
            Vector3 sp3 = cam.WorldToScreenPoint(new Vector3(world.x, 0.02f, world.z));
            float scale = Mathf.Max(0.35f, Mathf.Min(2.4f, 26f / Mathf.Max(4f, sp3.z)));
            float w = 86f * sizeScale * scale;
            sp.gameObject.SetActive(true);
            sp.rectTransform.sizeDelta = new Vector2(w, w * 0.5f);
            sp.rectTransform.anchoredPosition = local;
        }

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

            // 特效推进
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var it = _items[i];
                var img = it.img;
                if (img == null) { _items.RemoveAt(i); continue; }

                it.life -= dt;
                if (it.life <= 0f)
                {
                    RecycleFx(img);
                    _items.RemoveAt(i);
                    continue;
                }

                float t = 1f - it.life / it.maxLife;

                // 序列帧推进
                if (it.frames.Length > 1)
                {
                    int fi = Mathf.Min(it.frames.Length - 1, Mathf.FloorToInt(t * it.frames.Length));
                    if (fi != it.frame)
                    {
                        it.frame = fi;
                        img.sprite = it.frames[fi];
                    }
                }

                Vector2 local;
                if (!Project(it.world, out local))
                {
                    img.gameObject.SetActive(false);
                    continue;
                }
                img.gameObject.SetActive(true);
                img.rectTransform.anchoredPosition = new Vector2(local.x, local.y - t * it.rise);

                float grow = it.frames.Length > 1 ? 1f + t * 0.35f : 1f + t * 0.8f;
                float s = it.scale * grow * 0.75f;
                img.rectTransform.localScale = new Vector3(s, s, 1f);

                var col = img.color;
                col.a = it.frames.Length > 1 ? 1f - t * 0.25f : 1f - t;
                img.color = col;

                if (it.spin != 0f) img.rectTransform.Rotate(0f, 0f, it.spin * dt);
            }
        }
    }
}
