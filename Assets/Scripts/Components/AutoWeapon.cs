using System.Collections.Generic;
using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 自动武器：按「武器挂点 Mount」驱动，每个挂点持有一份 WeaponDef（数值/选敌形状/结算方式）。
    /// - 侧方排炮：左右各一门，直射最近单体（ImpactType.Direct）
    /// - 正面直射炮：商店加装后启用，炮弹飞到落点地面范围爆炸（ImpactType.Area）
    /// 扩展新装备攻击：WeaponDef 加工厂 → BuildMounts 挂炮口 → 如需新结算方式在 DamageKit 加方法，
    /// 本类的选敌/开火/弹体推进框架不用动。
    /// </summary>
    public class AutoWeapon : MonoBehaviour
    {
        [Header("炮弹模板（对象池）")]
        [SerializeField] private GameObject shellTpl;

        [Header("炮口")]
        [SerializeField] private Transform muzzleL;
        [SerializeField] private Transform muzzleR;
        [SerializeField] private Transform muzzleF;

        [Header("主相机：只对屏幕内可见敌人开火")]
        [SerializeField] private Camera cam;

        /// <summary>一个武器挂点：定义 + 炮口 + 运行时冷却/发炮序号</summary>
        private class Mount
        {
            public WeaponDef Def;
            public Transform Muzzle;
            public float Cd;
            public float InitCd;   // 开局/重开时的初始冷却（让多门炮错峰开火）
            public int Seq;
            // 该挂点是否需要装备解锁（正面炮要商店加装）
            public bool RequireFrontCannon;
        }

        private readonly List<Mount> _mounts = new List<Mount>();
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

        /// <summary>组装全部武器挂点（新装备在这里加一行即可）</summary>
        private void BuildMounts()
        {
            _mounts.Clear();
            _mounts.Add(new Mount { Def = WeaponDef.SideCannon(-1), Muzzle = muzzleL, Cd = 0.6f, InitCd = 0.6f });
            _mounts.Add(new Mount { Def = WeaponDef.SideCannon(1),  Muzzle = muzzleR, Cd = 1.2f, InitCd = 1.2f });
            _mounts.Add(new Mount { Def = WeaponDef.FrontCannon(),  Muzzle = muzzleF, Cd = 1.6f, InitCd = 1.6f, RequireFrontCannon = true });
        }

        private void ClearShots(object payload)
        {
            for (int i = 0; i < _shots.Count; i++)
                if (_shots[i] != null) ObjectPool.Despawn(_shots[i].gameObject);
            _shots.Clear();
            for (int i = 0; i < _mounts.Count; i++)
            {
                _mounts[i].Seq = 0;
                _mounts[i].Cd = _mounts[i].InitCd;
            }
        }

        /// <summary>取炮口世界坐标，未接线时回退到堡垒中心上方</summary>
        private Vector3 Muzzle(Transform node, Vector3 pos)
        {
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
        private EnemyUnit Nearest(Vector3 pos, int side, float cone, float blindFront = 0f, bool bossOnly = false)
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
            float bd = GameConfig.WeaponRange * GameConfig.WeaponRange;

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
            EnemyUnit normal = Nearest(pos, d.Side, d.Cone, blind);

            if (!d.BossFocus) return normal;
            int every = Mathf.Max(1, GameConfig.Survival.BossTargetEvery);
            if (m.Seq % every != 0) return normal;

            var boss = Nearest(pos, d.Side, d.Cone, blind, true);
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
            Vector3 from = Muzzle(m.Muzzle, transform.position);

            AudioKit.PlaySfx(d.FireSfx);
            Fx.PlayVfx(VfxKeys.FireBall, from.x, from.y, from.z, d.MuzzleFxScale);
            Fx.Shake(d.FireShake);

            if (shellTpl == null) return;
            var shell = ObjectPool.Spawn(shellTpl, from, Quaternion.identity, transform.parent);
            shell.transform.localScale = new Vector3(d.ShellScale, d.ShellScale, d.ShellScale);

            var sh = shell.GetComponent<Shell>();
            if (sh == null) sh = shell.AddComponent<Shell>();
            sh.Init(d, target, d.Damage * GS.DmgMul);
            _shots.Add(sh);
        }

        // ---------------- 主循环 ----------------

        private void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, GameConfig.MaxDelta);

            if (!GS.Over && !GS.Paused)
            {
                Vector3 pos = transform.position;
                for (int i = 0; i < _mounts.Count; i++)
                {
                    var m = _mounts[i];
                    // 需要装备解锁的挂点（正面炮）：没加装不转冷却也不开火
                    if (m.RequireFrontCannon && !GS.HasFrontCannon) continue;

                    m.Cd -= dt;
                    if (m.Cd > 0f) continue;

                    var t = PickTarget(m, pos);
                    if (t != null)
                    {
                        m.Cd = m.Def.Cooldown * GS.CdMul;
                        Fire(m, t);
                    }
                    else m.Cd = 0.25f; // 没目标时短间隔重试，不浪费一整轮冷却
                }
            }

            StepShells(dt);
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

                // 朝落点直线飞行；高度按剩余距离比例下压，落地时 y=0
                float step = d.ShellSpeed * dt;
                if (dl < 0.0001f) dl = 1f;
                sp.x += (dx / dl) * step;
                sp.z += (dz / dl) * step;
                float heightRatio = Mathf.Clamp01(dl / s.StartDist);
                sp.y = heightRatio * s.StartY;
                s.transform.position = sp;

                // 飞行火焰拖尾：沿弹道持续留火团，让正面炮轨迹清晰可见
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
