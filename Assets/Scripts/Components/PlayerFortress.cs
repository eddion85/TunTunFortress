using UnityEngine;
using UnityEngine.EventSystems;

namespace BattleFortress
{
    /// <summary>
    /// 玩家堡垒：摇杆移动、冲撞头锤、进化换外观。
    /// 对应 LayaAir 版 components/PlayerFortress.ts。
    /// 注意：这里不做 Reset，局内状态统一由 GameDirector 初始化，
    /// 否则组件启动顺序不定会覆盖掉跨关继承的养成数据。
    /// </summary>
    public class PlayerFortress : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private Camera cam;

        [Header("移动")]
        [SerializeField] private float speed = 6.2f;

        /// <summary>模型正前方若与 +Z 不一致，用这个角度修正</summary>
        [Header("朝向")]
        [SerializeField] private float yawOffset = 0f;

        [Header("四阶外观（同时只显示一个）")]
        [SerializeField] private GameObject tier0;
        [SerializeField] private GameObject tier1;
        [SerializeField] private GameObject tier2;
        [SerializeField] private GameObject tier3;

        [Header("点地移动（点击/按住屏幕空地自动走过去）")]
        [SerializeField] private bool useTapToMove = true;   // 关掉则回退到旧摇杆模式
        [SerializeField] private float arriveDistance = 0.35f; // 到点多少米内停下
        [SerializeField] private float markMinMove = 1.2f;    // 目标移动超过多少米才再播一次落点光圈

        private Vector3 _dir = new Vector3(0f, 0f, 1f);
        private float _dash;
        private float _cd;
        private float _evolveT;
        private float _evolveTick;
        private float _dashTick;
        private GameObject[] _tiers;
        private int _skinStage = -1;

        // 点地移动状态
        private static readonly Plane GroundPlane = new Plane(Vector3.up, Vector3.zero);
        private bool _hasTarget;
        private Vector3 _target;
        private Vector3 _lastMark;
        private bool _marked;
        private bool _joyHidden;

        [Header("LoL 式落点光标")]
        [SerializeField] private float markerLife = 0.5f;   // 光标存活秒数
        [SerializeField] private float markerSize = 2.4f;   // 光标世界直径（米）
        [SerializeField] private Color markerColor = new Color(0.55f, 1f, 0.55f, 1f); // LoL 移动绿
        private SpriteRenderer _marker;
        private float _markerT = -1f;
        private float _markerBaseScale = 1f;

        [Header("边界提示")]
        [SerializeField] private float edgeTipCooldown = 2.5f; // 同一边界提示最小间隔（秒）
        [SerializeField] private float edgeEps = 0.08f;       // 距边界多少米视为贴边
        private float _edgeTipT;

        // ---------------- 生命周期 ----------------
        private void OnEnable()
        {
            _tiers = new[] { tier0, tier1, tier2, tier3 };
            ApplyTier(GS.Stage);
            GameBus.On(GameEvents.Evolve, OnEvolve);
            GameBus.On(GameEvents.Skill, OnSkill);
            GameBus.On(GameEvents.Restart, OnRestart);
            GameBus.On(GameEvents.Revive, OnRevive);
        }

        private void OnDisable()
        {
            GameBus.Off(GameEvents.Evolve, OnEvolve);
            GameBus.Off(GameEvents.Skill, OnSkill);
            GameBus.Off(GameEvents.Restart, OnRestart);
            GameBus.Off(GameEvents.Revive, OnRevive);
        }

        private void Start()
        {
            // 开局强制出生在地图正中心，不依赖场景实例里保存的坐标
            transform.position = Vector3.zero;
            if (cam == null) cam = Camera.main;
            if (useTapToMove && !_joyHidden)
            {
                _joyHidden = true;
                var joy = GameObject.Find("Joystick");
                if (joy != null) joy.SetActive(false); // 点地模式下隐藏虚拟摇杆
            }
            BuildMoveMarker();
        }

        /// <summary>运行时创建一个平铺在地面上的落点光标（不写入场景，停止播放即销毁）</summary>
        private void BuildMoveMarker()
        {
            var spr = Resources.Load<Sprite>(VfxKeys.RingWave);
            if (spr == null) return;
            var go = new GameObject("TapMoveMarker");
            go.transform.SetParent(transform.parent, false); // 放在世界根下，不跟随战车
            _marker = go.AddComponent<SpriteRenderer>();
            _marker.sprite = spr;
            _marker.color = markerColor;
            _marker.sortingOrder = 8;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // XZ 地面平铺
            float baseSize = spr.bounds.size.x;
            _markerBaseScale = baseSize > 0.0001f ? markerSize / baseSize : 1f;
            go.transform.localScale = Vector3.one * _markerBaseScale;
            go.SetActive(false);
        }

        private void ShowMoveMarker(Vector3 p)
        {
            if (_marker == null) return;
            var mt = _marker.transform;
            mt.position = new Vector3(p.x, 0.12f, p.z);
            mt.localScale = Vector3.one * _markerBaseScale * 0.6f;
            _marker.color = markerColor;
            _marker.gameObject.SetActive(true);
            _markerT = 0f;
        }

        private void TickMoveMarker(float dt)
        {
            if (_markerT < 0f || _marker == null) return;
            _markerT += dt;
            float t = Mathf.Clamp01(_markerT / markerLife);
            // LoL 式：小圈快速弹出到略大，再整体淡出
            float s = Mathf.Lerp(0.6f, 1.08f, 1f - (1f - t) * (1f - t));
            _marker.transform.localScale = Vector3.one * _markerBaseScale * s;
            var c = markerColor;
            c.a = 1f - t;
            _marker.color = c;
            if (t >= 1f)
            {
                _markerT = -1f;
                _marker.gameObject.SetActive(false);
            }
        }

        private void HideMoveMarker()
        {
            _markerT = -1f;
            if (_marker != null) _marker.gameObject.SetActive(false);
        }

        /// <summary>按进化阶段切换堡垒外观：四个形态都在场景里，只做显隐</summary>
        private void ApplyTier(int stage)
        {
            if (stage == _skinStage) return;
            _skinStage = stage;
            if (_tiers == null) return;
            for (int i = 0; i < _tiers.Length; i++)
                if (_tiers[i] != null) _tiers[i].SetActive(i == stage);
        }

        // ---------------- 冲撞技能 ----------------
        public bool SkillReady() { return _cd <= 0f && !GS.Over && !GS.Paused; }
        public float SkillRatio() { return Mathf.Max(0f, _cd) / GameConfig.DashCd; }
        public bool Dashing() { return _dash > 0f; }
        /// <summary>进化金光期间无敌 [策划书 4.1]</summary>
        public bool Invincible() { return _evolveT > 0f; }

        private void OnSkill(object payload)
        {
            if (!SkillReady()) return;
            _cd = GameConfig.DashCd;
            _dash = 0.34f;
            AudioKit.PlaySfx(SfxKeys.Dash);
            var p = transform.position;
            Fx.PlayVfx(VfxKeys.RingWave, p.x, p.y + 0.3f, p.z, 2.6f);
            Fx.Shake(0.7f);
            GameBus.Emit(GameEvents.Float, "冲撞头锤!");
        }

        private void OnEvolve(object payload)
        {
            int stage = payload is int ? (int)payload : 0;
            float s = GameConfig.StageScale[Mathf.Clamp(stage, 0, 3)];
            transform.localScale = new Vector3(s, s, s);
            ApplyTier(stage);

            var pp = transform.position;
            Fx.PlayVfx(VfxKeys.EvolveSheet, pp.x, pp.y + 1.4f, pp.z, 5.5f);
            Fx.PlayVfx(VfxKeys.RingWave, pp.x, pp.y + 0.3f, pp.z, 5.0f);
            Fx.PlayVfx(VfxKeys.LightBeam, pp.x, pp.y + 2f, pp.z, 4.0f);
            Fx.Shake(2.0f);
            GameBus.Emit(GameEvents.Float, "进化 " + stage + " 阶!");

            // 金光变身 1.5s：期间无敌并持续放光效 [策划书 4.1]
            _evolveT = GameConfig.EvolveShowTime;
        }

        private void OnRestart(object payload)
        {
            // 按 GS.Stage 还原外观：重新开局时 stage 已清零自然回到 Tier0
            int st = Mathf.Clamp(GS.Stage, 0, 3);
            float s = GameConfig.StageScale[st];
            transform.localScale = new Vector3(s, s, s);
            transform.position = Vector3.zero;
            _dash = 0f;
            _cd = 0f;
            _evolveT = 0f;
            _hasTarget = false; _marked = false; HideMoveMarker();
            ApplyTier(st);
        }

        /// <summary>复活：保留进化形态与等级，回到场地中心、清空冲撞冷却并播一次光复演出</summary>
        private void OnRevive(object payload)
        {
            transform.position = Vector3.zero;
            _dash = 0f;
            _cd = 0f;
            _hasTarget = false; _marked = false; HideMoveMarker();
            var pp = transform.position;
            Fx.PlayVfx(VfxKeys.EvolveSheet, pp.x, pp.y + 1.4f, pp.z, 5.5f);
            Fx.PlayVfx(VfxKeys.RingWave, pp.x, pp.y + 0.3f, pp.z, 5.0f);
            Fx.PlayVfx(VfxKeys.LightBeam, pp.x, pp.y + 2f, pp.z, 4.0f);
            Fx.Shake(1.4f);
            GameBus.Emit(GameEvents.Float, "复活!");
        }

        // ---------------- 移动输入 ----------------
        /// <summary>旧模式：虚拟摇杆 → 世界方向（相对相机），圆形小死区，任意方向连续无量化</summary>
        private bool PollJoystick()
        {
            float ax = Joy.x;
            float ay = Joy.y;
            if (ax * ax + ay * ay <= 0.04f * 0.04f) return false;

            Vector3 f = cam != null ? cam.transform.forward : Vector3.forward;
            Vector3 r = cam != null ? cam.transform.right : Vector3.right;
            f.y = 0f; r.y = 0f;
            if (f.sqrMagnitude > 0.0001f) f.Normalize(); else f = Vector3.forward;
            if (r.sqrMagnitude > 0.0001f) r.Normalize(); else r = Vector3.right;

            // Laya 版 ay 向下为正：-ay 表示向上推摇杆时沿相机前方前进
            Vector3 d = r * ax + f * -ay;
            d.y = 0f;
            if (d.sqrMagnitude <= 0.0001f) return false;
            _dir = d.normalized;
            return true;
        }

        /// <summary>
        /// 新模式：点击/按住屏幕任意空地，射线打到 y=0 地面得到目标点，自动走过去。
        /// 点在 UI（按钮/面板/摇杆）上不触发；松手后保留目标点继续走到；到点自动停。
        /// </summary>
        private bool PollTapMove()
        {
            bool held;
            Vector2 screenPos;
            int fingerId = -1;

            if (Input.touchCount > 0)
            {
                Touch t = Input.GetTouch(0);
                held = t.phase != TouchPhase.Ended && t.phase != TouchPhase.Canceled;
                screenPos = t.position;
                fingerId = t.fingerId;
            }
            else
            {
                held = Input.GetMouseButton(0);
                screenPos = Input.mousePosition;
            }

            if (held && cam != null)
            {
                bool overUi = EventSystem.current != null &&
                    (fingerId >= 0
                        ? EventSystem.current.IsPointerOverGameObject(fingerId)
                        : EventSystem.current.IsPointerOverGameObject());
                if (!overUi)
                {
                    Ray ray = cam.ScreenPointToRay(screenPos);
                    if (GroundPlane.Raycast(ray, out float enter))
                    {
                        Vector3 p = ray.GetPoint(enter);
                        p.y = 0f;
                        float half = GameConfig.ArenaHalf;
                        p.x = Mathf.Clamp(p.x, -half, half);
                        p.z = Mathf.Clamp(p.z, -half, half);
                        _target = p;
                        _hasTarget = true;
                        if (!_marked || (p - _lastMark).sqrMagnitude > markMinMove * markMinMove)
                        {
                            _lastMark = p;
                            _marked = true;
                            ShowMoveMarker(p); // LoL 式落点光标
                        }
                    }
                }
            }

            if (!_hasTarget) return false;
            Vector3 cur = transform.position;
            Vector3 to = _target - cur;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist <= arriveDistance)
            {
                _hasTarget = false; // 到点停下
                _marked = false;
                HideMoveMarker();
                return false;
            }
            _dir = to / dist;
            return true;
        }

        // ---------------- 每帧 ----------------
        private void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, GameConfig.MaxDelta);
            if (_cd > 0f) _cd -= dt;
            if (_edgeTipT > 0f) _edgeTipT -= dt;
            if (GS.Over || GS.Paused) { HideMoveMarker(); return; }
            TickMoveMarker(dt);

            // 无敌 = 进化金光演出 或 复活保护（复活时长由配置驱动）
            GS.Invincible = _evolveT > 0f || GS.ReviveInvincibleT > 0f;
            if (_evolveT > 0f)
            {
                _evolveT -= dt;
                _evolveTick -= dt;
                if (_evolveTick <= 0f)
                {
                    _evolveTick = 0.18f;
                    var ep = transform.position;
                    Fx.PlayVfx(VfxKeys.StarSpark,
                        ep.x + (Random.value * 2f - 1f), ep.y + 1f + Random.value, ep.z + (Random.value * 2f - 1f), 1.2f);
                    Fx.PlayVfx(VfxKeys.LightBeam, ep.x, ep.y + 1.5f, ep.z, 2.2f);
                }
            }

            // 移动输入：默认点地移动；useTapToMove=false 时回退旧摇杆
            bool moving = useTapToMove ? PollTapMove() : PollJoystick();

            if (_dash > 0f)
            {
                _dash -= dt;
                _dashTick -= dt;
                if (_dashTick <= 0f)
                {
                    _dashTick = 0.13f;
                    var cp = transform.position;
                    Fx.PlayVfx(VfxKeys.SpeedLine, cp.x, cp.y + 1f, cp.z, 2.2f);
                }
            }

            if (!moving && _dash <= 0f) return;

            float boost = _dash > 0f ? 3.1f : 1f;
            float sp = speed * GS.SpeedMul * boost;
            Vector3 pos = transform.position;
            float half = GameConfig.ArenaHalf;

            // 边界钳制：贴边时保留切向移动，避免斜向顶墙时完全卡死
            pos.x = Mathf.Clamp(pos.x + _dir.x * sp * dt, -half, half);
            pos.z = Mathf.Clamp(pos.z + _dir.z * sp * dt, -half, half);
            pos.y = 0f;
            transform.position = pos;

            // 朝墙走且已贴边 → 非阻断提示（自动上浮淡出，不暂停游戏；节流防刷屏）
            bool hitX = (_dir.x > 0.01f && pos.x >= half - edgeEps) || (_dir.x < -0.01f && pos.x <= -half + edgeEps);
            bool hitZ = (_dir.z > 0.01f && pos.z >= half - edgeEps) || (_dir.z < -0.01f && pos.z <= -half + edgeEps);
            if ((hitX || hitZ) && _edgeTipT <= 0f)
            {
                _edgeTipT = edgeTipCooldown;
                GameBus.Emit(GameEvents.Float, "已到地图边界");
            }

            if (_dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(_dir, Vector3.up) * Quaternion.Euler(0f, yawOffset, 0f);

            // 冲撞头锤：碾碎路径上的敌人 [策划书 4.1]
            if (_dash > 0f)
            {
                for (int i = 0; i < Registry.Enemies.Count; i++)
                {
                    var e = Registry.Enemies[i];
                    if (e == null || e.Dead || e.IsBoss) continue;
                    var ep = e.transform.position;
                    float dx = ep.x - pos.x;
                    float dz = ep.z - pos.z;
                    if (dx * dx + dz * dz < 2.4f * 2.4f)
                    {
                        float cd = GameConfig.SideDmg * 1.5f * GS.DmgMul * GS.SizeFactor(e.Size());
                        e.Hp -= cd;
                        Fx.PopDmg(ep, cd, true);
                        Fx.PlayVfx(VfxKeys.HitSheet, ep.x, 1f, ep.z, 1.9f);
                        Fx.Shake(0.5f);
                    }
                }
            }
        }
    }
}
