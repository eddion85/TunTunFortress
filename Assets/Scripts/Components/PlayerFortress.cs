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

        [Header("点击落点光标（仅引导用，表现逻辑见 TapMoveMarker）")]
        [SerializeField] private float markerLife = 0.45f;  // 光标存活秒数
        [SerializeField] private float markerSize = 3.0f;   // 光标世界直径（米）
        [SerializeField] private Color markerColor = new Color(1f, 0.86f, 0.32f, 1f); // 暖黄引导色
        private TapMoveMarker _marker;

        [Header("边界提示")]
        [SerializeField] private float edgeTipCooldown = 2.5f; // 同一边界提示最小间隔（秒）
        [SerializeField] private float edgeEps = 0.08f;       // 距边界多少米视为贴边
        private float _edgeTipT;

        private float _trailT;   // 移动冒烟拖尾吐烟计时

        [Header("正面直射炮外观（模型内开关节点默认隐藏，HasFrontCannon 解锁后显示）")]
        [SerializeField] private string cannonNodeName = "dbdp";           // 形态模型内炮开关节点的名字（Blender 合成时命名）
        private bool _cannonBuilt;
        // 形态模型内部的炮开关节点（如 Tier3 的 dbdp），默认隐藏，按 GS.HasFrontCannon 显隐
        private readonly System.Collections.Generic.List<GameObject> _cannonNodes = new System.Collections.Generic.List<GameObject>();

        // ---------------- 生命周期 ----------------
        private void OnEnable()
        {
            _tiers = new[] { tier0, tier1, tier2, tier3 };
            BuildFrontCannon(); // 尽早隐藏模型内炮节点（dbdp），保证首帧就不显示
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
            _marker = TapMoveMarker.Create(transform.parent, markerLife, markerSize, markerColor);
            BuildFrontCannon();
        }

        /// <summary>
        /// 在各形态模型内部递归查找炮开关节点（名字由 cannonNodeName 指定，默认 dbdp）。
        /// 炮外观已在模型里合成，找到后默认隐藏，由 GS.HasFrontCannon 统一控制显隐，
        /// 节点挂在模型层级下，会随车体系放、缩放、转向，无需额外坐标计算。
        /// </summary>
        private void BuildFrontCannon()
        {
            if (_cannonBuilt) return;
            _cannonBuilt = true;
            _cannonNodes.Clear();
            if (_tiers == null || string.IsNullOrEmpty(cannonNodeName)) return;
            for (int i = 0; i < _tiers.Length; i++)
            {
                if (_tiers[i] == null) continue;
                var node = FindDeepChild(_tiers[i].transform, cannonNodeName);
                if (node != null)
                {
                    node.gameObject.SetActive(false); // 默认不显示，加装正面炮（GS.HasFrontCannon）后才显示
                    _cannonNodes.Add(node.gameObject);
                }
            }
        }

        /// <summary>递归按名字查找子节点（glTFast 导入的模型层级较深）</summary>
        private static Transform FindDeepChild(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var hit = FindDeepChild(root.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>按解锁状态显隐模型内炮节点（升级卡/商店/一阶进化解锁，重开关卡时隐藏）</summary>
        private void SyncFrontCannon()
        {
            bool show = GS.HasFrontCannon;
            for (int i = 0; i < _cannonNodes.Count; i++)
            {
                var n = _cannonNodes[i];
                if (n != null && n.activeSelf != show) n.SetActive(show);
            }
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
            _dash = GameConfig.DashTime;
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
            _hasTarget = false; _marked = false; _marker?.Hide();
            ApplyTier(st);
        }

        /// <summary>复活：保留进化形态与等级，回到场地中心、清空冲撞冷却并播一次光复演出</summary>
        private void OnRevive(object payload)
        {
            transform.position = Vector3.zero;
            _dash = 0f;
            _cd = 0f;
            _hasTarget = false; _marked = false; _marker?.Hide();
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
                            _marker.Show(p); // LoL 式落点光标
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
                _marker.Hide();
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
            SyncFrontCannon(); // 升级卡/商店/进化解锁后立刻显示，重开后自动隐藏
            if (GS.Over || GS.Paused) { _marker?.Hide(); return; }
            _marker?.Tick(dt);

            // 无敌 = 进化金光演出 或 复活保护（复活时长由配置驱动）
            GS.Invincible = _evolveT > 0f || GS.ReviveInvincibleT > 0f;
            if (_evolveT > 0f)
            {
                _evolveT -= dt;
                _evolveTick -= dt;
                if (_evolveTick <= 0f)
                {
                    _evolveTick = GameConfig.EvolveFxInterval;
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
                    _dashTick = GameConfig.DashFxInterval;
                    var cp = transform.position;
                    Fx.PlayVfx(VfxKeys.SpeedLine, cp.x, cp.y + 1f, cp.z, 2.2f);
                }
            }

            if (!moving && _dash <= 0f) return;

            float boost = _dash > 0f ? GameConfig.DashSpeedMul : 1f;
            float sp = speed * GS.SpeedMul * boost;
            Vector3 pos = transform.position;
            float half = GameConfig.ArenaHalf;

            // 边界钳制：贴边时保留切向移动，避免斜向顶墙时完全卡死
            pos.x = Mathf.Clamp(pos.x + _dir.x * sp * dt, -half, half);
            pos.z = Mathf.Clamp(pos.z + _dir.z * sp * dt, -half, half);
            pos.y = 0f;
            transform.position = pos;

            // 移动冒烟拖尾：按固定间隔从车尾吐一团烟，贴地扩散淡出（参数全部配置驱动）
            _trailT -= dt;
            if (_trailT <= 0f)
            {
                bool dashing = _dash > 0f;
                _trailT = dashing ? GameConfig.TrailDashInterval : GameConfig.TrailInterval;
                Vector3 side = new Vector3(-_dir.z, 0f, _dir.x);
                float jitter = (Random.value * 2f - 1f) * GameConfig.TrailJitter;
                Vector3 smokePos = pos - _dir * GameConfig.TrailBack + side * jitter;
                smokePos.y = 0.25f;
                // 体型越大烟团略大；冲撞时烟更浓更大；每团加少量随机避免机械重复
                int st = Mathf.Clamp(GS.Stage, 0, GameConfig.StageScale.Length - 1);
                float sizeMul = Mathf.Sqrt(GameConfig.StageScale[st]) * (dashing ? 1.5f : 1f) * (0.85f + Random.value * 0.3f);
                Fx.PlayTrailSmoke(smokePos, GameConfig.TrailScale * sizeMul);
            }

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
                    float dashR = GameConfig.DashHitRadius;
                    if (dx * dx + dz * dz < dashR * dashR)
                    {
                        float cd = GameConfig.SideDmg * GameConfig.DashDmgMul * GS.DmgMul * GS.SizeFactor(e.Size());
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
