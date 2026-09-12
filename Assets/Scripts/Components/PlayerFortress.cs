using UnityEngine;
using System.Collections.Generic;
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
        private float _trackT;   // 地面车辙落印计时（左右各一道，沿真实轨迹）

        [Header("正面直射炮外观（模型内开关节点默认隐藏，HasFrontCannon 解锁后显示）")]
        [SerializeField] private string cannonNodeName = "dbdp";           // 形态模型内炮开关节点的名字（Blender 合成时命名）
        private bool _cannonBuilt;
        // 形态模型内部的炮开关节点（如 Tier3 的 dbdp），默认隐藏，按 GS.HasFrontCannon 显隐
        private readonly System.Collections.Generic.List<GameObject> _cannonNodes = new System.Collections.Generic.List<GameObject>();

        [Header("武器挂装（通用 WeaponMountSystem：指定挂点父节点即可换装）")]
        [SerializeField] private string topSlotId = "Slot_Top";      // 车顶挂点 id（同时也是模型内节点名）
        [SerializeField, Range(0.1f, 1f)] private float weaponWidthRatio = 0.5f; // 自动尺寸：武器宽度占当前形态宽度比例

        [Header("车侧弩箭（奖励弹窗解锁：每侧1把，升星后每侧2把，沿前后排列、朝外射击）")]
        [SerializeField] private string sideLeftSlotName = "Slot_Side_Left";
        [SerializeField] private string sideRightSlotName = "Slot_Side_Right";
        [SerializeField, Range(0.1f, 1f)] private float crossbowWidthRatio = 0.7f; // 弩宽度占车体宽度比例（也是双弩前后间距），上限1保证不超出车体
        [SerializeField] private Vector3 crossbowEulerLeft = new Vector3(0f, 0f, -90f);  // 左弩朝外：弩身模型 +Y 转到世界 +X，弩臂竖直
        [SerializeField] private Vector3 crossbowEulerRight = new Vector3(0f, 0f, 90f);  // 右弩朝外：模型 +Y 转到世界 -X
        private const int MaxCrossbowPerSide = 2;


        [Header("车头攻城锤（Slot_Front，4 阶，正前方直线突刺）")]
        [SerializeField] private string frontSlotId = "Slot_Front";
        // 攻城锤模型长轴是 Y，按 Y 量长度并旋转 X+90 让长轴对正车头 +Z；数值在 GameConfig
        private static readonly Vector3 RamEuler = GameConfig.RamLocalEuler;

        private WeaponMountSystem _weapons;
        private AutoWeapon _auto;                       // 战斗层：外观装上后把武器注册给它驱动开火
        private int _rocketCombatLevel = -1;            // 已接入战斗层的火箭炮等级，-1=未接入
        private int _ramCombatLevel = -1;               // 已接入战斗层的攻城锤等级，-1=未接入
        private readonly HashSet<string> _combatRacks = new HashSet<string>(); // 已接入战斗层的弩箭挂点
        // 车顶同一槽位互斥：火箭炮显示时隐藏模型内 dbdp 顶炮，避免重叠
        private bool _rocketWantsShow;
#if UNITY_EDITOR
        [Header("调试（仅 Editor：勾选后绕过 GS 直接显示，用于对位置/大小/切等级）")]
        [SerializeField] private bool debugShowRocket = false;
        [SerializeField, Range(0, 3)] private int debugRocketLevel = 0;
        [SerializeField] private bool debugShowCrossbow = false; // 强制两侧满配弩箭，用于对位/朝向检查
        [SerializeField] private bool debugShowRam = false;      // 强制显示攻城锤，用于对位/突刺检查
        [SerializeField, Range(0, 3)] private int debugRamLevel = 0;
#endif

        // ---------------- 生命周期 ----------------
        private void OnEnable()
        {
            _tiers = new[] { tier0, tier1, tier2, tier3 };
            BuildFrontCannon(); // 尽早隐藏模型内炮节点（dbdp），保证首帧就不显示
            if (_auto == null) _auto = GetComponent<AutoWeapon>();
            BuildWeaponRigs();  // 注册武器挂点（默认空挂点，装备后才显示模型）
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
            ReleaseCombatMounts();
            _weapons?.Dispose();
            _weapons = null;
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
            // 车顶槽位互斥：火箭炮显示时隐藏模型内 dbdp 顶炮
            bool show = GS.HasFrontCannon && !_rocketWantsShow;
            for (int i = 0; i < _cannonNodes.Count; i++)
            {
                var n = _cannonNodes[i];
                if (n != null && n.activeSelf != show) n.SetActive(show);
            }
        }

        /// <summary>当前激活形态的根（形态切换时挂点委托实时取它）</summary>
        private GameObject ActiveTier()
        {
            if (_tiers == null) return null;
            int i = Mathf.Clamp(_skinStage, 0, _tiers.Length - 1);
            return _tiers[i];
        }

        /// <summary>
        /// 注册武器挂点：车顶 Slot_Top。父节点与尺寸参考根都通过委托实时解析，
        /// 进化换形态后 WeaponMountSystem.RebindAll 会把武器平移到新形态的同名挂点。
        /// 以后加新槽位武器：在这里再 Bind 一个 id 即可。
        /// </summary>
        private void BuildWeaponRigs()
        {
            if (_weapons != null || _tiers == null) return;
            _weapons = new WeaponMountSystem();
            _weapons.Bind(topSlotId,
                () => // 挂点父节点：当前激活形态内按名深找
                {
                    var tier = ActiveTier();
                    return tier != null ? FindDeepChild(tier.transform, topSlotId) : null;
                },
                () => ActiveTier()?.transform, // 尺寸参考根：当前形态模型
                weaponWidthRatio);

            // 车头攻城锤：长轴沿模型 Y，按 Y 量长度，旋转后长轴对正车头 +Z
            _weapons.Bind(frontSlotId,
                () =>
                {
                    var tier = ActiveTier();
                    return tier != null ? FindDeepChild(tier.transform, frontSlotId) : null;
                },
                () => ActiveTier()?.transform,
                GameConfig.RamLengthRatio, RamEuler, 1,
                new Vector3(GameConfig.RamThickMul, 1f, GameConfig.RamThickMul)); // 只加粗垂直长轴的 X/Z，长度不变

            // 车侧弩箭：每侧最多 MaxCrossbowPerSide 把，各自一个排架锚点（挂点子节点），沿车体前后排列
            for (int i = 0; i < MaxCrossbowPerSide; i++)
            {
                int idx = i; // 闭包捕获副本
                _weapons.Bind(SideRackId(true, idx),
                    () => RackAnchor(sideLeftSlotName, idx, crossbowEulerLeft),
                    () => ActiveTier()?.transform, crossbowWidthRatio, crossbowEulerLeft);
                _weapons.Bind(SideRackId(false, idx),
                    () => RackAnchor(sideRightSlotName, idx, crossbowEulerRight),
                    () => ActiveTier()?.transform, crossbowWidthRatio, crossbowEulerRight);
            }
        }

        private static string SideRackId(bool left, int idx) => (left ? "Crossbow_L" : "Crossbow_R") + idx;

        /// <summary>
        /// 在侧挂点下取/建第 idx 把弩的排架锚点：两把时沿车体前后（世界 +Z/-Z）各偏半个弩宽，
        /// 锚点旋转决定弩的朝向。换形态后锚点随新挂点按需重建，武器由 RebindAll 平移过来。
        /// </summary>
        private Transform RackAnchor(string slotName, int idx, Vector3 euler)
        {
            var tier = ActiveTier();
            if (tier == null) return null;
            var slot = FindDeepChild(tier.transform, slotName);
            if (slot == null) return null;

            const string anchorPrefix = "rack_";
            var anchor = slot.Find(anchorPrefix + idx);
            if (anchor == null)
            {
                var go = new GameObject(anchorPrefix + idx);
                anchor = go.transform;
                anchor.SetParent(slot, false);
            }

            // 总把数决定排布：1 把居中；2 把前后各半（间距=弩的世界宽度，与自动缩放同口径）
            float bodyWidth = WeaponMount.MeasureBodyWidth(tier.transform);
            float spacing = bodyWidth * crossbowWidthRatio;
            float along = MaxCrossbowPerSide <= 1 ? 0f : (idx == 0 ? -0.5f : 0.5f) * spacing;
            anchor.localPosition = slot.InverseTransformDirection(Vector3.forward * along);
            anchor.localRotation = Quaternion.identity; // 朝向由 WeaponMount 的 localEuler 承担
            return anchor;
        }

        /// <summary>当前每侧弩箭数量：达到解锁阶段后每升一阶每侧 +1，封顶 MaxCrossbowPerSide</summary>
        private int CrossbowsPerSide()
        {
            int n = GS.CrossbowLevel;
#if UNITY_EDITOR
            if (debugShowCrossbow) n = MaxCrossbowPerSide;
#endif

            return Mathf.Clamp(n, 0, MaxCrossbowPerSide);
        }

        /// <summary>按 GS.CrossbowLevel 同步两侧弩箭：达到数量就 Equip，不足就卸下多余的</summary>
        private void SyncCrossbows()
        {
            int perSide = CrossbowsPerSide();
            for (int i = 0; i < MaxCrossbowPerSide; i++)
            {
                // 模型 Slot_Side_Left 在 +X 侧 → 选敌半场 side=+1；Right 在 -X 侧 → -1
                SyncOneRack(SideRackId(true, i), i < perSide, +1);
                SyncOneRack(SideRackId(false, i), i < perSide, -1);
            }
        }

        /// <summary>销毁/失活时把动态武器从战斗层注销（固定炮不受影响）</summary>
        private void ReleaseCombatMounts()
        {
            if (_auto == null) return;
            _auto.UnregisterMount(topSlotId);
            _auto.UnregisterMount(frontSlotId);
            foreach (var id in _combatRacks) _auto.UnregisterMount(id);
            _combatRacks.Clear();
            _rocketCombatLevel = -1;
            _ramCombatLevel = -1;
        }

        /// <summary>单个弩架：外观装上/卸下的同时，向 AutoWeapon 注册/注销对应战斗挂点</summary>
        private void SyncOneRack(string rackId, bool active, int side)
        {
            if (active)
            {
                bool visualChanged = _weapons != null && _weapons.Equip(rackId, WeaponCatalog.Crossbow);
                if (visualChanged || !_combatRacks.Contains(rackId))
                {
                    _auto?.RegisterMount(rackId, WeaponDef.Crossbow(side),
                        () => _weapons != null ? _weapons.InstanceOf(rackId) : null);
                    _combatRacks.Add(rackId);
                }
            }
            else if (_weapons != null && _weapons.Unequip(rackId))
            {
                _auto?.UnregisterMount(rackId);
                _combatRacks.Remove(rackId);
            }
        }

        /// <summary>按装备状态同步车顶火箭炮显隐与等级（商店加装/升级、重开都会经过这里）</summary>
        private void SyncTopRocket()
        {
            bool show;
            int level;
#if UNITY_EDITOR
            // Editor 调试开关：勾选后绕过 GS，直接在 Inspector 切等级看效果
            if (debugShowRocket) { show = true; level = debugRocketLevel; }
            else
#endif
            { show = GS.HasRocketLauncher; level = GS.RocketLevel; }

            _rocketWantsShow = show;
            // 外观：Equip 同路径 no-op、换等级自动换模型；战斗层：刚装上/等级变化时注册（热更新数值）
            if (show)
            {
                bool visualChanged = _weapons != null && _weapons.Equip(topSlotId, WeaponCatalog.Rocket(level));
                if (visualChanged || _rocketCombatLevel != level)
                {
                    _auto?.RegisterMount(topSlotId, WeaponDef.Rocket(level),
                        () => _weapons != null ? _weapons.InstanceOf(topSlotId) : null);
                    _rocketCombatLevel = level;
                }
            }
            else
            {
                bool removed = _weapons != null && _weapons.Unequip(topSlotId);
                if (removed || _rocketCombatLevel >= 0)
                {
                    _auto?.UnregisterMount(topSlotId);
                    _rocketCombatLevel = -1;
                }
            }
        }

        /// <summary>按装备状态同步车头攻城锤（商店加装/升级、重开都会经过这里）</summary>
        private void SyncRam()
        {
            bool show;
            int level;
#if UNITY_EDITOR
            if (debugShowRam) { show = true; level = debugRamLevel; }
            else
#endif
            { show = GS.HasBatteringRam; level = GS.RamLevel; }

            if (show)
            {
                bool visualChanged = _weapons != null && _weapons.Equip(frontSlotId, WeaponCatalog.Ram(level));
                if (visualChanged || _ramCombatLevel != level)
                {
                    _auto?.RegisterMount(frontSlotId, WeaponDef.Ram(level),
                        () => _weapons != null ? _weapons.InstanceOf(frontSlotId) : null);
                    _ramCombatLevel = level;
                }
            }
            else
            {
                bool removed = _weapons != null && _weapons.Unequip(frontSlotId);
                if (removed || _ramCombatLevel >= 0)
                {
                    _auto?.UnregisterMount(frontSlotId);
                    _ramCombatLevel = -1;
                }
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
            _weapons?.RebindAll(); // 换形态后把所有挂点武器平移到新形态的同名挂点
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
            // 装了攻城锤时，点冲撞按钮同时打出一次前刺（攻城锤自己的冷却没好则只冲撞不突刺）
            _auto?.TryManual(frontSlotId);
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
            SyncTopRocket();    // 先算火箭炮需求，供 dbdp 互斥判断
            SyncRam();          // 车头攻城锤挂装与战斗挂点同步
            SyncFrontCannon(); // 升级卡/商店/进化解锁后立刻显示，重开后自动隐藏
            SyncCrossbows();   // 按进化阶段同步两侧弩箭数量
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

            // 地面车辙：车尾左右各落一个土黄印，位置取自真实行驶轨迹，转弯时轨迹自然弯曲
            _trackT -= dt;
            if (_trackT <= 0f)
            {
                _trackT = GameConfig.TrackInterval;
                int stk = Mathf.Clamp(GS.Stage, 0, GameConfig.StageScale.Length - 1);
                float bodyMul = Mathf.Sqrt(GameConfig.StageScale[stk]); // 体型越大车辙间距/印子越大
                Vector3 sideVec = new Vector3(-_dir.z, 0f, _dir.x);
                float sideDist = GameConfig.TrackSide * bodyMul;
                float backDist = GameConfig.TrackBack * bodyMul;
                float screenAng = MoveScreenAngle(_dir);
                float markScale = GameConfig.TrackScale * bodyMul;
                for (int k = -1; k <= 1; k += 2)
                {
                    Vector3 mp = pos - _dir * backDist + sideVec * (k * sideDist);
                    mp.y = 0.12f;
                    Fx.PlayTrackMark(mp, screenAng, markScale, GameConfig.TrackStretch);
                }
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
        /// <summary>世界水平方向 → UI 屏幕角度（让车辙长条沿行驶方向；UI 局部 +Y 对齐屏幕移动方向）</summary>
        private float MoveScreenAngle(Vector3 worldDir)
        {
            Camera c = cam != null ? cam : Camera.main;
            if (c == null || worldDir.sqrMagnitude < 0.0001f) return 0f;
            Vector3 p0 = transform.position;
            Vector2 s0 = c.WorldToScreenPoint(p0);
            Vector2 s1 = c.WorldToScreenPoint(p0 + worldDir);
            return Vector2.SignedAngle(Vector2.up, s1 - s0);
        }
    }
}
