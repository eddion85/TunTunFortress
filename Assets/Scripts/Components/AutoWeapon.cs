using System;
using System.Collections.Generic;
using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 自动武器：按「武器挂点 Mount」驱动，每个挂点持有一份 WeaponDef（数值/选敌形状/结算方式）。
    /// - 场景固定炮：侧方排炮左右各一门（直射单体）、正面直射炮（商店加装后启用，落地范围爆炸）
    /// - 动态挂载炮：由 WeaponMountSystem 换装的武器（车顶火箭炮、车侧弩箭…）运行时
    ///   调 RegisterMount/UnregisterMount 接入本框架，选敌/开火/飞弹/后坐动画全部复用，不用各写一套。
    /// 扩展新装备攻击：WeaponDef 加工厂 → 固定炮在 BuildMounts 挂炮口 / 动态炮在换装处 RegisterMount
    /// → 如需新结算方式在 DamageKit 加方法，本类的选敌/开火/弹体推进框架不用动。
    /// </summary>
    public class AutoWeapon : MonoBehaviour
    {
        [Header("炮弹模板（对象池）")]
        [SerializeField] private GameObject shellTpl;
        [SerializeField] private GameObject shellTplArrow;   // 弩箭专用箭矢弹体（不填则回退 shellTpl）

        [Header("炮口")]
        [SerializeField] private Transform muzzleL;
        [SerializeField] private Transform muzzleR;
        [SerializeField] private Transform muzzleF;

        [Header("主相机：只对屏幕内可见敌人开火")]
        [SerializeField] private Camera cam;

        /// <summary>一个武器挂点：定义 + 炮口解析 + 运行时冷却/发炮序号/后坐动画状态</summary>
        private class Mount
        {
            public string Id;
            public WeaponDef Def;
            public Func<Transform> MuzzleGetter; // 炮口节点（动态武器随换装/换形态实时解析）
            public Func<bool> Gate;              // 启用门控（返回 false 不转冷却也不开火；null = 永远启用）
            public float Cd;
            public float InitCd;   // 开局/重开时的初始冷却（让多门炮错峰开火）
            public int Seq;

            // 开火后坐动画
            public Transform RecoilRoot;
            public Vector3 RecoilBase;
            public Vector3 RecoilOffset;
            public Vector3 RecoilVel;

            // 近战前刺动画（攻城锤：沿局部 +Z 快速前冲再回位）
            public Transform ThrustRoot;
            public Vector3 ThrustBase;
            public float ThrustT;
            public float ThrustDur;
            public float ThrustLocalDist;
        }

        private readonly List<Mount> _fixed = new List<Mount>();
        private readonly Dictionary<string, Mount> _dynamic = new Dictionary<string, Mount>();
        private readonly List<Mount> _tick = new List<Mount>(); // 每帧复用以避免 GC
        private readonly List<Shell> _shots = new List<Shell>();

        private void OnEnable()
        {
            GameBus.On(GameEvents.Restart, ClearShots);
            BuildMounts();
        }

        private void OnDisable()
        {
            GameBus.Off(GameEvents.Restart, ClearShots);
        }

        private void Start()
        {
            if (cam == null) cam = Camera.main;
        }

        // ---------------- 动态挂载接入（WeaponMountSystem 换装武器用） ----------------

        /// <summary>
        /// 注册（或覆盖）一个动态武器挂点：同一 id 重复注册 = 热更新定义与炮口解析。
        /// </summary>
        /// <param name="id">挂点唯一 id（建议与 WeaponMountSystem 的 socketId 一致）</param>
        /// <param name="def">武器数值定义（WeaponDef 工厂产出）</param>
        /// <param name="muzzleGetter">炮口节点解析（一般返回挂载武器实例根）</param>
        /// <param name="gate">启用门控，null = 注册即启用</param>
        public void RegisterMount(string id, WeaponDef def, Func<Transform> muzzleGetter, Func<bool> gate = null)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (!_dynamic.TryGetValue(id, out var m))
            {
                // 初始冷却错峰：多把弩同时挂上时不会挤在同一帧齐射
                float stagger = 0.35f + _dynamic.Count * 0.25f;
                m = new Mount { InitCd = stagger, Cd = stagger };
                _dynamic[id] = m;
            }
            m.Id = id;
            m.Def = def;
            m.MuzzleGetter = muzzleGetter;
            m.Gate = gate;
        }

        /// <summary>热更新已注册挂点的武器定义（如火箭炮升级，换 def 不重置冷却节奏）</summary>
        public void UpdateDef(string id, WeaponDef def)
        {
            if (!string.IsNullOrEmpty(id) && _dynamic.TryGetValue(id, out var m)) m.Def = def;
        }

        /// <summary>注销动态挂点（武器卸下/重开），并把后坐位移复位</summary>
        public void UnregisterMount(string id)
        {
            if (string.IsNullOrEmpty(id) || !_dynamic.TryGetValue(id, out var m)) return;
            ResetRecoil(m);
            _dynamic.Remove(id);
        }

        /// <summary>
        /// 手动触发一个动态武器挂点（如点「冲撞」按钮触发攻城锤）。
        /// 近战突刺不要求锁定目标，沿车头朝向直接打出；其他手动武器仍需选到目标才开火。
        /// 冷却中/门控未过/未装备返回 false。冷却规则与自动武器一致（MinCd 兜底）。
        /// </summary>
        public bool TryManual(string id)
        {
            if (GS.Over || GS.Paused) return false;
            if (string.IsNullOrEmpty(id) || !_dynamic.TryGetValue(id, out var m) || m.Def == null) return false;
            if (m.Gate != null && !m.Gate()) return false;
            if (m.Cd > 0f) return false;

            if (m.Def.Impact == ImpactType.Thrust)
            {
                m.Cd = Mathf.Max(m.Def.MinCd, m.Def.Cooldown * GS.CdMul);
                DoThrust(m);
                return true;
            }

            var target = PickTarget(m, transform.position);
            if (target == null) return false;
            m.Cd = Mathf.Max(m.Def.MinCd, m.Def.Cooldown * GS.CdMul);
            Fire(m, target);
            return true;
        }

        /// <summary>组装场景固定武器挂点（新固定装备在这里加一行即可）</summary>
        private void BuildMounts()
        {
            _fixed.Clear();
            _fixed.Add(new Mount
            {
                Id = "fixed_side_l", Def = WeaponDef.SideCannon(-1),
                MuzzleGetter = () => muzzleL, Cd = 0.6f, InitCd = 0.6f
            });
            _fixed.Add(new Mount
            {
                Id = "fixed_side_r", Def = WeaponDef.SideCannon(1),
                MuzzleGetter = () => muzzleR, Cd = 1.2f, InitCd = 1.2f
            });
            _fixed.Add(new Mount
            {
                Id = "fixed_front", Def = WeaponDef.FrontCannon(),
                MuzzleGetter = () => muzzleF, Cd = 1.6f, InitCd = 1.6f,
                Gate = () => GS.HasFrontCannon
            });
        }

        private void ClearShots(object payload)
        {
            for (int i = 0; i < _shots.Count; i++)
                if (_shots[i] != null) ObjectPool.Despawn(_shots[i].gameObject);
            _shots.Clear();
            ResetAllCd(_fixed);
            ResetAllCd(_dynamic.Values);
        }

        private static void ResetAllCd(IEnumerable<Mount> mounts)
        {
            foreach (var m in mounts)
            {
                m.Seq = 0;
                m.Cd = m.InitCd;
                ResetRecoil(m);
            }
        }

        /// <summary>取炮口世界坐标，未接线时回退到堡垒中心上方</summary>
        private Vector3 Muzzle(Mount m, Vector3 pos)
        {
            var node = m.MuzzleGetter != null ? m.MuzzleGetter() : null;
            if (node != null) return node.position;
            return new Vector3(pos.x, pos.y + 1.1f, pos.z);
        }

        // ---------------- 选敌 ----------------

        /// <summary>
        /// 选敌。
        /// </summary>
        /// <param name="pos">堡垒位置</param>
        /// <param name="side">-1 = 只挑左半边、1 = 只挑右半边、0 = 不限</param>
        /// <param name="cone">&gt;0 时只挑正前方锥形内的目标</param>
        /// <param name="blindFront">&gt;0 时回避正前方盲区（cos 半角阈值），把正面目标让给正面炮</param>
        /// <param name="bossOnly">只挑 Boss（Boss 体积大，不受左右半场限制，避免被近身小兵永久「抢火」）</param>
        private EnemyUnit Nearest(Vector3 pos, int side, float cone, float range,
            float blindFront = 0f, bool bossOnly = false)
        {
            float fx = 0f, fz = 1f, rx = 1f, rz = 0f;
            if (side != 0 || cone > 0f)
            {
                Vector3 f = transform.forward; f.y = 0f; f.Normalize();
                Vector3 r = transform.right; r.y = 0f; r.Normalize();
                fx = f.x; fz = f.z;
                rx = r.x; rz = r.z;
            }

            EnemyUnit best = null;
            float bd = range * range;

            for (int i = 0; i < Registry.Enemies.Count; i++)
            {
                var e = Registry.Enemies[i];
                if (e == null || e.Dead || !e.gameObject.activeInHierarchy) continue;
                if (bossOnly && !e.IsBoss) continue;

                Vector3 p = e.transform.position;
                float dx = p.x - pos.x;
                float dz = p.z - pos.z;
                float d = dx * dx + dz * dz;
                if (d >= bd) continue;

                // 只对「看得到」的敌人开火：必须在屏幕范围内，
                // 避免玩家明明没看到敌人、炮却自动把屏幕外目标打死的违和感
                if (!IsOnScreen(p)) continue;

                // 点名 Boss 时不看左右半场（Boss 是大型目标，两侧炮都该能打）
                if (!bossOnly && side != 0)
                {
                    if ((dx * rx + dz * rz) * side < 0f) continue;
                }
                // 侧炮正面盲区：与车头夹角小于盲区半角的目标不打，统一留给正面直射炮
                if (blindFront > 0f)
                {
                    float db = Mathf.Sqrt(d);
                    if (db > 0.001f && (dx * fx + dz * fz) / db >= blindFront) continue;
                }
                if (cone > 0f)
                {
                    float dl = Mathf.Sqrt(d);
                    if (dl < 0.0001f) dl = 1f;
                    if ((dx * fx + dz * fz) / dl < cone) continue;
                }

                bd = d;
                best = e;
            }
            return best;
        }

        /// <summary>按挂点定义选敌：支持左右半场 / 正前锥形，以及隔发点名 Boss</summary>
        private EnemyUnit PickTarget(Mount m, Vector3 pos)
        {
            var d = m.Def;
            m.Seq++;
            // 正面盲区只在已加装正面炮后生效；没加装时侧炮保持 360° 覆盖，避免正前方火力真空
            float blind = d.BlindFront > 0f && GS.HasFrontCannon ? d.BlindFront : 0f;
            EnemyUnit normal = Nearest(pos, d.Side, d.Cone, d.Range, blind);

            if (!d.BossFocus) return normal;
            int every = Mathf.Max(1, GameConfig.Survival.BossTargetEvery);
            if (m.Seq % every != 0) return normal;

            var boss = Nearest(pos, d.Side, d.Cone, d.Range, blind, true);
            return boss != null ? boss : normal;
        }

        /// <summary>敌人是否落在屏幕可见范围内（留 15% 边距）</summary>
        private bool IsOnScreen(Vector3 p)
        {
            if (cam == null) return true;
            Vector3 vp = cam.WorldToScreenPoint(new Vector3(p.x, 1.2f, p.z));
            if (vp.z <= 0f) return false;
            float mx = Screen.width * 0.15f;
            float my = Screen.height * 0.15f;
            return vp.x > -mx && vp.x < Screen.width + mx && vp.y > -my && vp.y < Screen.height + my;
        }

        // ---------------- 开火 ----------------

        private void Fire(Mount m, EnemyUnit target)
        {
            var d = m.Def;

            // 近战突刺：不出弹体，瞬间判定正前方攻击带 + 播放前刺动画
            if (d.Impact == ImpactType.Thrust)
            {
                DoThrust(m);
                return;
            }

            Vector3 from = Muzzle(m, transform.position);

            if (!string.IsNullOrEmpty(d.FireSfx)) AudioKit.PlaySfx(d.FireSfx);
            if (d.MuzzleFxScale > 0f) Fx.PlayVfx(VfxKeys.FireBall, from.x, from.y, from.z, d.MuzzleFxScale);
            Fx.Shake(d.FireShake);
            KickRecoil(m);

            // 弩箭用箭矢模型，其余用炮弹；未配置箭矢模板时回退默认炮弹
            var tpl = d.ProjKind == ProjectileKind.Arrow && shellTplArrow != null ? shellTplArrow : shellTpl;
            if (tpl == null) return;
            var shell = ObjectPool.Spawn(tpl, from, Quaternion.identity, transform.parent);
            shell.transform.localScale = new Vector3(d.ShellScale, d.ShellScale, d.ShellScale);

            var sh = shell.GetComponent<Shell>();
            if (sh == null) sh = shell.AddComponent<Shell>();
            sh.Init(d, target, d.Damage * GS.DmgMul);
            _shots.Add(sh);
        }

        // ---------------- 近战突刺：瞬间直线结算 + 武器前刺动画 ----------------

        private void DoThrust(Mount m)
        {
            var d = m.Def;
            Vector3 fwd = transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            fwd.Normalize();

            // 以玩家为起点的正前方矩形攻击带，瞬间结算
            DamageKit.Thrust(transform.position, fwd, d.Range, d.Damage * GS.DmgMul, d);

            // 前刺动画：武器沿局部 +Z 前冲「自身长度 × ThrustMul（>1/2）」再回位
            var root = m.MuzzleGetter != null ? m.MuzzleGetter() : null;
            if (root == null || root.parent == null) return;
            float worldLen = WeaponMount.CombinedAxisLength(root, 2); // 已旋转：世界 Z = 攻城锤长轴
            if (worldLen < 0.0001f) worldLen = WeaponMount.CombinedAxisLength(root, 1);
            float parentScale = Mathf.Max(0.000001f, root.parent.lossyScale.z);
            m.ThrustRoot = root;
            m.ThrustBase = root.localPosition;
            m.ThrustLocalDist = d.ThrustMul * worldLen / parentScale;
            m.ThrustDur = Mathf.Max(0.05f, d.ThrustTime);
            m.ThrustT = 0f;
        }

        // ---------------- 开火后坐动画：沿武器局部 -Y 瞬间回缩，再弹簧回位 ----------------

        private void KickRecoil(Mount m)
        {
            if (m.Def.RecoilKick <= 0f) return;
            var root = m.MuzzleGetter != null ? m.MuzzleGetter() : null;
            if (root == null) return;
            m.RecoilRoot = root;
            m.RecoilBase = root.localPosition;
            m.RecoilOffset = new Vector3(0f, -m.Def.RecoilKick, 0f);
            m.RecoilVel = Vector3.zero;
        }

        private void StepRecoils(float dt)
        {
            StepRecoils(_fixed, dt);
            StepRecoils(_dynamic.Values, dt);
        }

        private void StepRecoils(IEnumerable<Mount> mounts, float dt)
        {
            foreach (var m in mounts)
            {
                if (m.RecoilRoot == null) continue;
                if (m.RecoilOffset.sqrMagnitude < 0.0000001f)
                {
                    ResetRecoil(m);
                    continue;
                }
                float t = Mathf.Max(0.02f, m.Def.RecoilSpring);
                m.RecoilOffset = Vector3.SmoothDamp(m.RecoilOffset, Vector3.zero, ref m.RecoilVel, t,
                    Mathf.Infinity, dt);
                m.RecoilRoot.localPosition = m.RecoilBase + m.RecoilOffset;
            }
        }

        private static void ResetRecoil(Mount m)
        {
            if (m.RecoilRoot != null) m.RecoilRoot.localPosition = m.RecoilBase;
            m.RecoilRoot = null;
            m.RecoilOffset = Vector3.zero;
            m.RecoilVel = Vector3.zero;
            if (m.ThrustRoot != null) m.ThrustRoot.localPosition = m.ThrustBase;
            m.ThrustRoot = null;
            m.ThrustT = 0f;
            m.ThrustLocalDist = 0f;
        }

        /// <summary>推进所有前刺动画：sin 曲线前冲至最远再回位（半程到顶，总时长 ThrustDur）</summary>
        private void StepThrusts(float dt)
        {
            StepThrusts(_fixed, dt);
            StepThrusts(_dynamic.Values, dt);
        }

        private void StepThrusts(IEnumerable<Mount> mounts, float dt)
        {
            foreach (var m in mounts)
            {
                if (m.ThrustRoot == null) continue;
                m.ThrustT += dt;
                float p = Mathf.Clamp01(m.ThrustT / m.ThrustDur);
                // sin(πp)：p=0 在原位、p=0.5 前冲到 ThrustLocalDist、p=1 回位
                float ext = Mathf.Sin(Mathf.PI * p) * m.ThrustLocalDist;
                m.ThrustRoot.localPosition = m.ThrustBase + new Vector3(0f, 0f, ext);
                if (p >= 1f)
                {
                    m.ThrustRoot.localPosition = m.ThrustBase;
                    m.ThrustRoot = null;
                }
            }
        }

        // ---------------- 主循环 ----------------

        private void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, GameConfig.MaxDelta);

            if (!GS.Over && !GS.Paused)
            {
                Vector3 pos = transform.position;
                _tick.Clear();
                _tick.AddRange(_fixed);
                _tick.AddRange(_dynamic.Values);
                for (int i = 0; i < _tick.Count; i++)
                {
                    var m = _tick[i];
                    if (m.Def == null || m.MuzzleGetter == null) continue;
                    // 门控未通过（如正面炮未加装）：不转冷却也不开火
                    if (m.Gate != null && !m.Gate()) continue;

                    m.Cd -= dt;
                    if (m.Cd > 0f) continue;
                    // 手动武器（攻城锤）：冷却照常恢复，但不自动选敌开火，只响应 TryManual
                    if (m.Def.Manual) continue;

                    var t = PickTarget(m, pos);
                    if (t != null)
                    {
                        // 间隔 = 基础间隔 × 全局攻速，再用 MinCd 兜底，保证不会「看见敌人就连发」
                        m.Cd = Mathf.Max(m.Def.MinCd, m.Def.Cooldown * GS.CdMul);
                        Fire(m, t);
                    }
                    else m.Cd = 0.25f; // 没目标时短间隔重试，不浪费一整轮冷却
                }
            }

            StepShells(dt);
            StepRecoils(dt);
            StepThrusts(dt);
        }

        /// <summary>推进所有在飞炮弹：Direct 追单体，Area 追地面落点并在到达/到期时爆炸</summary>
        private void StepShells(float dt)
        {
            float arrive = GameConfig.ShellArriveDist;

            for (int i = _shots.Count - 1; i >= 0; i--)
            {
                var s = _shots[i];
                if (s == null) { _shots.RemoveAt(i); continue; }

                var d = s.Def;
                s.Life -= dt;

                bool alive = s.TargetAlive;
                // 弹体始终追着当前目标的地面位置；Area 弹在目标死亡后保留最后落点继续飞
                if (alive) s.Aim = Shell.GroundPoint(s.Target.transform.position);

                Vector3 sp = s.transform.position;
                float dx = s.Aim.x - sp.x;
                float dz = s.Aim.z - sp.z;
                float dl = Mathf.Sqrt(dx * dx + dz * dz);
                bool reached = dl <= arrive;
                bool expired = s.Life <= 0f;

                if (d.Impact == ImpactType.Direct)
                {
                    // 直射弹：目标没了/寿命到 → 直接消失；贴到目标 → 单体结算
                    if (!alive || expired)
                    {
                        ObjectPool.Despawn(s.gameObject);
                        _shots.RemoveAt(i);
                        continue;
                    }
                    if (reached)
                    {
                        var tp = s.Target.transform.position;
                        DamageKit.DirectHit(s.Target, s.Dmg, tp, d.BigDamageNumber,
                            d.ImpactFxScale, d.RingFxScale, d.ImpactShake, d.ImpactSfx);
                        ObjectPool.Despawn(s.gameObject);
                        _shots.RemoveAt(i);
                        continue;
                    }
                }
                else // ImpactType.Area：飞到落点地面爆炸；目标中途死了也照样炸最后落点
                {
                    if (reached || expired)
                    {
                        Vector3 impact = reached ? Shell.GroundPoint(s.Aim) : Shell.GroundPoint(sp);
                        DamageKit.Explode(impact, d.ImpactRadius, s.Dmg, d.BigDamageNumber,
                            d.ImpactFxScale, d.RingFxScale, d.ImpactShake, d.ImpactSfx);
                        ObjectPool.Despawn(s.gameObject);
                        _shots.RemoveAt(i);
                        continue;
                    }
                }

                // 朝落点直线推进水平位置；高度走抛物线：出膛高度线性归零 + 4h·t(1-t) 拱顶，
                // ArcHeight>0（如 Slot_Top 火箭炮）时先上升、过半程后俯冲突击；=0 时退化为直线下压
                float step = d.ShellSpeed * dt;
                if (dl < 0.0001f) dl = 1f;
                Vector3 before = sp;
                sp.x += (dx / dl) * step;
                sp.z += (dz / dl) * step;
                float t = 1f - Mathf.Clamp01(dl / s.StartDist); // 0=出膛 1=落地
                sp.y = s.StartY * (1f - t) + 4f * d.ArcHeight * t * (1f - t);
                s.transform.position = sp;

                // 弹体顺着瞬时速度方向（上升时仰、下落时俯），抛物线更自然
                Vector3 vel = sp - before;
                if (vel.sqrMagnitude > 0.000001f)
                    s.transform.rotation = Quaternion.LookRotation(vel.normalized, Vector3.up);

                // 飞行火焰拖尾：沿弹道持续留火团，让范围炮轨迹清晰可见
                if (d.FlightTrail && d.TrailInterval > 0f)
                {
                    s.TrailT -= dt;
                    if (s.TrailT <= 0f)
                    {
                        s.TrailT = d.TrailInterval;
                        // trail:true 走独立拖尾对象池，不挤占受击/爆炸等战斗特效名额
                        Fx.PlayVfx(VfxKeys.FireBall, sp, d.TrailFxScale, -1f, -1f, -1f, -1f, true);
                    }
                }
            }
        }
    }
}
