using System.Collections.Generic;
using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 敌人攻击/结算方式。新增攻击效果时在这里加一个枚举值，并在 EnemyCombat.Tick 里实现对应分支，
    /// 之后任何敌种/Boss 都能在自己的 EnemyAttackDef 里复用，不用改刷怪主循环。
    /// </summary>
    public enum EnemyAttackKind
    {
        /// <summary>不攻击（羊）</summary>
        None = 0,
        /// <summary>贴身接触伤害（牛/农夫/骑兵）</summary>
        Contact = 1,
        /// <summary>发射投射物（弓箭手射箭、侧炮车齐射……弹体/炮口/齐射都由数据决定）</summary>
        Projectile = 2,
        /// <summary>近身范围 AoE（Boss 砸地）</summary>
        Slam = 3
    }

    /// <summary>投射物瞄准方式</summary>
    public enum EnemyFireMode
    {
        /// <summary>每个炮口直接瞄向玩家（弓箭手）</summary>
        Aimed = 0,
        /// <summary>只能沿车身左右两侧向外打（侧炮车）：只用朝向玩家那一侧的炮口，方向为车身侧向</summary>
        Sideways = 1
    }

    /// <summary>
    /// 一次敌人攻击的完整数据描述（纯数据）。和武器的 WeaponDef 对称：
    /// 移动 AI（EnemyAi）与攻击方式（本类）正交组合，例如「绕侧走位 + 侧向炮齐射」。
    /// 炮口/炮塔/炮管的程序化表现由 EnemyAttackRig 按这里的节点名驱动。
    /// </summary>
    public sealed class EnemyAttackDef
    {
        public EnemyAttackKind Kind = EnemyAttackKind.None;
        public float Cd;              // 攻击间隔（秒）
        public float Range;           // 触发距离（米）

        // ---- Projectile ----
        public EnemyFireMode FireMode = EnemyFireMode.Aimed;
        public string ProjPath;       // 弹体 prefab 的 Resources 路径
        public int Volley = 1;        // 每个炮口一轮发射几发
        public float SpreadAngle;     // 单炮口齐射总张角（度）：均摊到各发，0=直瞄
        public float ProjSpeed;       // 弹速（米/秒）
        public float ProjLife;        // 弹体存活（秒）
        public float ProjScale = 1f;  // 弹体缩放
        public string MuzzleNode;     // 炮口子节点名（同名全部收集，如左右各 4 个），空=自身中心
        public float MuzzleHeight = 1f;
        public float BroadsideAngle;  // Sideways：车身与玩家夹角偏离正侧位不超过该角度才开火

        // ---- 攻击挂点表现（EnemyAttackRig） ----
        public bool AimAtPlayer;                  // 炮塔节点是否转向玩家
        public string TurretNode;                 // 转向玩家的炮塔节点名
        public string BarrelNode;                 // 后坐炮管节点名
        public float RecoilDist;                  // 后坐距离（米）
        public float RecoilTime;                  // 后坐回位时长（秒）
        public string[] HideNodes;                // 生成时隐藏的子节点（模型自带血条/状态特效）

        // ---- Slam ----
        public float SlamRadius;      // AoE 触发/伤害半径

        public string Sfx;            // 出手音效（SfxKeys，可空）
    }

    /// <summary>
    /// 敌人攻击执行器：每帧由 EnemySpawner 主循环对每个敌人调用一次 Tick，
    /// 只处理「怎么打」，不处理移动。Contact 的贴身结算留在主循环（要复用吞噬/命中特效判定）。
    /// </summary>
    public static class EnemyCombat
    {
        private const string ProjDir = "prefabs/proj/";
        private static readonly Dictionary<string, GameObject> _projCache = new Dictionary<string, GameObject>();

        // ---------------- 攻击方式工厂（数值走 GameConfig，新攻击效果在这里加工厂） ----------------

        public static EnemyAttackDef None() => new EnemyAttackDef { Kind = EnemyAttackKind.None };

        public static EnemyAttackDef Contact() => new EnemyAttackDef
        {
            Kind = EnemyAttackKind.Contact
        };

        /// <summary>车辆系小怪的贴身撞击：同样是接触伤害，但隐藏模型自带血条/状态特效节点</summary>
        public static EnemyAttackDef ContactVehicle() => new EnemyAttackDef
        {
            Kind = EnemyAttackKind.Contact,
            HideNodes = new[] { "UI_Elements", "VisualEffects" }
        };

        /// <summary>弓箭手：单发直瞄箭矢，保持射程带由 EnemyAi.RangedKite 负责</summary>
        public static EnemyAttackDef Archer() => new EnemyAttackDef
        {
            Kind = EnemyAttackKind.Projectile,
            FireMode = EnemyFireMode.Aimed,
            Cd = GameConfig.Ai.ArcherShootCd,
            Range = GameConfig.Ai.ArcherShootRange,
            ProjPath = ProjDir + "Proj_Arrow",
            Volley = 1, SpreadAngle = 0f,
            ProjSpeed = GameConfig.Ai.ArrowSpeed,
            ProjLife = GameConfig.Ai.ArrowLife,
            ProjScale = 1f,
            MuzzleNode = null,
            MuzzleHeight = 1f
        };

        /// <summary>侧炮车：只能左右侧向开火，绕到玩家并排侧位后用朝玩家那一侧的 4 个炮口齐射</summary>
        public static EnemyAttackDef SideCannon() => new EnemyAttackDef
        {
            Kind = EnemyAttackKind.Projectile,
            FireMode = EnemyFireMode.Sideways,
            Cd = GameConfig.Ai.EnemySideCannonCd,
            Range = GameConfig.Ai.EnemySideCannonRange,
            ProjPath = ProjDir + "Proj_CannonBall",
            Volley = 1,
            SpreadAngle = GameConfig.Ai.EnemySideCannonSpread,
            ProjSpeed = GameConfig.Ai.EnemySideCannonProjSpeed,
            ProjLife = GameConfig.Ai.EnemySideCannonProjLife,
            ProjScale = GameConfig.Ai.EnemySideCannonProjScale,
            MuzzleNode = GameConfig.Ai.SideShooterMuzzleNode,
            MuzzleHeight = GameConfig.Ai.EnemySideCannonMuzzleHeight,
            BroadsideAngle = GameConfig.Ai.SideShooterBroadside,
            AimAtPlayer = false, // 侧炮固定朝车身两侧，不转向
            TurretNode = null,
            BarrelNode = GameConfig.Ai.SideShooterBarrelNode,
            RecoilDist = GameConfig.Ai.SideShooterRecoilDist,
            RecoilTime = GameConfig.Ai.SideShooterRecoilTime,
            HideNodes = new[] { "UI_Elements", "VisualEffects" },
            Sfx = SfxKeys.Cannon
        };

        /// <summary>
        /// 通用炮塔（迫击炮/小炮塔/弓箭塔等一切 AttackModule 朝炮）：炮塔转向玩家，
        /// 每个 AttackInstantiationPoint 炮口朝玩家发射；炮管 Cannon_Holder 后坐。
        /// projPath=弹体 Resources 路径，多炮口模型（如弓箭塔 4 炮口）自动齐射。
        /// </summary>
        public static EnemyAttackDef Turret(string projPath, float cd, float range,
            float projSpeed, float projLife, float projScale, float spread = 0f) => new EnemyAttackDef
        {
            Kind = EnemyAttackKind.Projectile,
            FireMode = EnemyFireMode.Aimed,
            Cd = cd, Range = range,
            ProjPath = ProjDir + projPath,
            Volley = 1, SpreadAngle = spread,
            ProjSpeed = projSpeed, ProjLife = projLife, ProjScale = projScale,
            MuzzleNode = GameConfig.Ai.EnemyMuzzleNode,
            MuzzleHeight = GameConfig.Ai.EnemyTurretMuzzleHeight,
            AimAtPlayer = true,
            TurretNode = GameConfig.Ai.EnemyTurretNode,
            BarrelNode = GameConfig.Ai.EnemyBarrelNode,
            RecoilDist = GameConfig.Ai.EnemyTurretRecoilDist,
            RecoilTime = GameConfig.Ai.EnemyTurretRecoilTime,
            HideNodes = new[] { "UI_Elements", "VisualEffects" },
            Sfx = SfxKeys.Cannon
        };

        /// <summary>Boss 砸地：近身 AoE，半径/间隔由参数（GameConfig 或 Boss 定义）给</summary>
        public static EnemyAttackDef Slam(float cd, float radius, string sfx = null) => new EnemyAttackDef
        {
            Kind = EnemyAttackKind.Slam,
            Cd = cd,
            Range = radius,
            SlamRadius = radius,
            Sfx = sfx ?? SfxKeys.ExplosionBig
        };

        // ---------------- 每帧结算 ----------------
        public static void Tick(EnemyUnit e, float dx, float dz, float dl, float dt, Transform parent)
        {
            var def = e.Def != null ? e.Def.Combat : null;
            if (def == null || def.Kind == EnemyAttackKind.None || def.Kind == EnemyAttackKind.Contact) return;

            switch (def.Kind)
            {
                case EnemyAttackKind.Projectile:
                    e.ShootCd -= dt;
                    if (dl > def.Range || e.ShootCd > 0f) break;
                    // 侧向炮必须先把车身侧过来（与玩家近似并排），没到位不开火也不进冷却
                    if (def.FireMode == EnemyFireMode.Sideways && !BroadsideReady(e, dx, dz, dl)) break;
                    e.ShootCd = def.Cd;
                    FireVolley(e, def, dx, dz, dl, parent);
                    break;

                case EnemyAttackKind.Slam:
                    e.SlamCd -= dt;
                    if (dl <= def.SlamRadius && e.SlamCd <= 0f)
                    {
                        e.SlamCd = def.Cd;
                        DoSlam(e, def);
                    }
                    break;
            }
        }

        /// <summary>Sideways 开火姿态判定：玩家方向与车身正侧方向的夹角是否在 BroadsideAngle 内</summary>
        private static bool BroadsideReady(EnemyUnit e, float dx, float dz, float dl)
        {
            var fwd = e.transform.forward; fwd.y = 0f;
            float fl = Mathf.Sqrt(fwd.x * fwd.x + fwd.z * fwd.z);
            if (fl < 0.0001f) return false;
            float dotF = (fwd.x * (dx / dl) + fwd.z * (dz / dl)) / fl; // 玩家方向与车头夹角余弦
            // 正侧位时 dotF≈0；偏离正侧位越多 |dotF| 越大
            float tol = Mathf.Cos(Mathf.PI * 0.5f - e.Def.Combat.BroadsideAngle * Mathf.Deg2Rad);
            return Mathf.Abs(dotF) <= tol;
        }

        // ---------------- Projectile ----------------
        private static void FireVolley(EnemyUnit e, EnemyAttackDef def, float dx, float dz, float dl, Transform parent)
        {
            var tpl = LoadProj(def.ProjPath);
            if (tpl == null) return;

            var rig = e.GetComponent<EnemyAttackRig>();
            int volley = Mathf.Max(1, def.Volley);
            float dmg = e.Damage() * GS.Profile().dmgMul;

            if (def.FireMode == EnemyFireMode.Sideways)
                FireSideways(e, def, rig, tpl, volley, dmg, parent, dx, dz, dl);
            else
                FireAimed(e, def, rig, tpl, volley, dmg, parent, dx, dz);

            // 炮管后坐
            if (rig != null) rig.Recoil(def.RecoilDist, def.RecoilTime);
            if (!string.IsNullOrEmpty(def.Sfx)) AudioKit.PlaySfx(def.Sfx);
        }

        /// <summary>直瞄模式：每个炮口朝玩家方向发射（无炮口节点时从自身中心）</summary>
        private static void FireAimed(EnemyUnit e, EnemyAttackDef def, EnemyAttackRig rig,
            GameObject tpl, int volley, float dmg, Transform parent, float dx, float dz)
        {
            var origins = new List<Vector3>();
            if (rig != null && rig.MuzzleCount > 0)
                for (int m = 0; m < rig.MuzzleCount; m++)
                {
                    var node = rig.GetMuzzle(m);
                    origins.Add(node != null ? node.position : e.transform.position);
                }
            else
            {
                var c = e.transform.position;
                origins.Add(new Vector3(c.x, def.MuzzleHeight, c.z));
            }

            Vector3 target = e.transform.position + new Vector3(dx, 0f, dz);
            for (int o = 0; o < origins.Count; o++)
            {
                float baseAng = Mathf.Atan2(target.x - origins[o].x, target.z - origins[o].z);
                Emit(tpl, parent, origins[o], baseAng, volley, def, dmg);
            }
        }

        /// <summary>侧向模式：判断玩家在车身哪一侧，只用那一侧炮口，沿车身侧向向外发射</summary>
        private static void FireSideways(EnemyUnit e, EnemyAttackDef def, EnemyAttackRig rig,
            GameObject tpl, int volley, float dmg, Transform parent, float dx, float dz, float dl)
        {
            // 车身前向与左侧向量（left = cross(up, forward) = (fz, -fx)）
            var fwd = e.transform.forward; fwd.y = 0f;
            float fl = Mathf.Max(0.0001f, Mathf.Sqrt(fwd.x * fwd.x + fwd.z * fwd.z));
            float fx = fwd.x / fl, fz = fwd.z / fl;
            float lx = fz, lz = -fx;

            // 玩家位于左侧还是右侧
            float sideDot = (dx / dl) * lx + (dz / dl) * lz;
            int side = sideDot >= 0f ? 1 : -1;
            float baseAng = Mathf.Atan2(lx * side, lz * side); // 朝该侧向外的水平角

            var origins = new List<Vector3>();
            if (rig != null)
            {
                rig.GetSideMuzzles(side > 0, origins);
            }
            if (origins.Count == 0)
                origins.Add(new Vector3(e.transform.position.x, def.MuzzleHeight, e.transform.position.z));

            for (int o = 0; o < origins.Count; o++)
                Emit(tpl, parent, origins[o], baseAng, volley, def, dmg);
        }

        /// <summary>按基准角与散射夹角发射 volley 发弹体</summary>
        private static void Emit(GameObject tpl, Transform parent, Vector3 origin, float baseAng,
            int volley, EnemyAttackDef def, float dmg)
        {
            for (int v = 0; v < volley; v++)
            {
                float off = volley > 1
                    ? (v - (volley - 1) * 0.5f) * (def.SpreadAngle * Mathf.Deg2Rad / (volley - 1))
                    : 0f;
                float ang = baseAng + off;
                float dirX = Mathf.Sin(ang), dirZ = Mathf.Cos(ang);

                var go = ObjectPool.Spawn(tpl, origin, Quaternion.identity, parent);
                go.transform.localScale = new Vector3(def.ProjScale, def.ProjScale, def.ProjScale);
                var proj = go.GetComponent<Projectile>();
                if (proj == null) proj = go.AddComponent<Projectile>();
                proj.Init(dirX, dirZ, dmg, def.ProjSpeed, def.ProjLife);
                go.transform.rotation = Quaternion.LookRotation(new Vector3(dirX, 0f, dirZ), Vector3.up);
                Registry.Projectiles.Add(proj);
            }
        }

        // ---------------- Slam ----------------
        private static void DoSlam(EnemyUnit e, EnemyAttackDef def)
        {
            GS.Damage(e.Damage() * GS.Profile().dmgMul);
            var p = e.transform.position;
            Fx.PlayVfx(VfxKeys.RingWave, p.x, 0.3f, p.z, GameConfig.Ai.BossSlamRingFx);
            Fx.PlayVfx(VfxKeys.ExplosionSheet, p.x, 1f, p.z, GameConfig.Ai.BossSlamExplosionFx);
            Fx.Shake(GameConfig.Ai.BossSlamShake);
            if (e.Animator != null)
            {
                BossMotion.Play(e, e.Def.AnimAttack);
                e.AttackT = GameConfig.Ai.BossAttackAnimTime;
            }
            if (!string.IsNullOrEmpty(def.Sfx)) AudioKit.PlaySfx(def.Sfx);
        }

        // ---------------- 工具 ----------------
        private static GameObject LoadProj(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (_projCache.TryGetValue(path, out var tpl)) return tpl;
            tpl = Resources.Load<GameObject>(path);
            _projCache[path] = tpl;
            return tpl;
        }
    }
}
