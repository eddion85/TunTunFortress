namespace BattleFortress
{
    /// <summary>
    /// 普通兵的移动 AI 模式（新增行为时在这里扩展，EnemySteer 里加对应分支）。
    /// </summary>
    public enum EnemyAi
    {
        /// <summary>直线追击（农夫/奶牛/侧炮车）</summary>
        Chase = 0,
        /// <summary>近逃远追、中间游荡（羊）</summary>
        FleeWander = 1,
        /// <summary>保持射程带风筝（弓箭手的走位，攻击由 EnemyCombat 负责）</summary>
        RangedKite = 2,
        /// <summary>远距绕侧、贴近直冲（骑兵）</summary>
        Flank = 3,
        /// <summary>绕到玩家左/右侧并排位置，到位后减速停住，用侧向炮开火（侧炮车）</summary>
        SideCombat = 4
    }

    /// <summary>
    /// 一个敌人种类/Boss 的完整定义（纯数据，不含逻辑）——与武器的 WeaponDef 对称。
    /// 移动方式看 Ai，攻击方式看 Combat（EnemyAttackDef），两者正交可任意组合。
    /// 新增敌人只需：1) 模型 prefab 放进 Resources 并填 PrefabPath；2) 在 EnemyCatalog 加一个工厂；
    /// 3) 普通兵在 GameConfig.Survival 的 TierUnlock/MobTable 补解锁时间与权重。刷怪/AI/结算/掉落全部复用。
    /// </summary>
    public sealed class EnemyDef
    {
        public string Key;
        public bool IsBoss;

        // ---------------- 数值 ----------------
        public float Hp;
        public float Dmg;
        public float Exp;
        public float ExpMin = -1f, ExpMax = -1f; // 浮动经验区间（羊），<0 表示用固定 Exp
        public int Prog;                         // 击杀金币 prog / 吞噬进度贡献
        public float Speed;
        public int Size;                         // 体积档位（玩家 Stage 需 ≥ 它才能吞）
        public float Scale = 1f;                 // 模型缩放
        public bool HasWheels;                    // true=挂 EnemyWheelRig 播车轮滚动（模型需有 Wheels 节点）

        // ---------------- 行为 / 攻击（正交组合） ----------------
        public EnemyAi Ai = EnemyAi.Chase;
        public EnemyAttackDef Combat;            // 攻击方式：None/Contact/Projectile/Slam，由 EnemyCombat 执行

        // ---------------- 模型与动画（Resources 路径；动画片段名空串=静态模型无骨骼动画） ----------------
        public string PrefabPath;
        public bool HasAnimator;                 // true=死亡走 die 动画再回收
        public string AnimIdle = "idle";
        public string AnimRun = "run";
        public string AnimAttack = "attack";
        public string AnimHit = "hit";
        public string AnimDie = "die";

        // ---------------- Boss 专属 ----------------
        public float Phase2At = 0.5f;            // 血量低于该比例进入二阶段提速
        public float AppearTime;                 // 该 Boss 从第几秒起可能出场（多 Boss 时按时间解锁）

        /// <summary>击杀经验：配了浮动区间就随机，否则固定 Exp</summary>
        public float RollExp()
        {
            if (ExpMax > ExpMin && ExpMin >= 0f)
                return ExpMin + UnityEngine.Random.value * (ExpMax - ExpMin);
            return Exp;
        }
    }

    /// <summary>
    /// 敌人注册表：普通兵按数组顺序决定解锁档位/权重下标；Boss 表按 AppearTime 选择。
    /// 与 WeaponCatalog 同风格，业务代码只认这里，不散落字符串/数值。
    /// </summary>
    public static class EnemyCatalog
    {
        private const string MobDir = "prefabs/enemy/";

        // ---------------- 普通兵工厂（顺序=档位/权重下标，勿随意调换） ----------------
        public static EnemyDef Sheep() => new EnemyDef
        {
            Key = "sheep", Hp = 3, Dmg = 0, Exp = 6, ExpMin = 4, ExpMax = 8, Prog = 2,
            Speed = 2.4f, Size = 0, Scale = 0.9f,
            Ai = EnemyAi.FleeWander, Combat = EnemyCombat.None(),
            PrefabPath = MobDir + "Enemy_Sheep"
        };

        public static EnemyDef Cow() => new EnemyDef
        {
            Key = "cow", Hp = 8, Dmg = 4, Exp = 11, Prog = 5,
            Speed = 2.6f, Size = 1, Scale = 1.1f,
            Ai = EnemyAi.Chase, Combat = EnemyCombat.Contact(),
            PrefabPath = MobDir + "Enemy_Cow"
        };

        public static EnemyDef Farmer() => new EnemyDef
        {
            Key = "farmer", Hp = 5, Dmg = 3, Exp = 7, Prog = 4,
            Speed = 2.6f, Size = 1, Scale = 1.0f,
            Ai = EnemyAi.Chase, Combat = EnemyCombat.Contact(),
            PrefabPath = MobDir + "Enemy_Farmer"
        };

        public static EnemyDef Archer() => new EnemyDef
        {
            Key = "archer", Hp = 9, Dmg = 5, Exp = 14, Prog = 6,
            Speed = 2.4f, Size = 1, Scale = 1.0f,
            Ai = EnemyAi.RangedKite, Combat = EnemyCombat.Archer(),
            PrefabPath = MobDir + "Enemy_Archer"
        };

        public static EnemyDef Rider() => new EnemyDef
        {
            Key = "rider", Hp = 14, Dmg = 7, Exp = 18, Prog = 8,
            Speed = 3.9f, Size = 2, Scale = 1.15f,
            Ai = EnemyAi.Flank, Combat = EnemyCombat.Contact(),
            PrefabPath = MobDir + "Enemy_Rider"
        };

        /// <summary>
        /// 侧炮车 Agent_SideShooter：直线逼近，进入射程后 AttackModules 左右 8 个炮口齐射炮弹；
        /// 炮塔转向玩家、炮管后坐（EnemyAttackRig 按节点名驱动，数值在 GameConfig.Ai.SideShooter*）。
        /// </summary>
        public static EnemyDef Guncar() => new EnemyDef
        {
            Key = "guncar", Hp = 300, Dmg = 4, Exp = 22, Prog = 10,
            Speed = 2.2f, Size = 2, Scale = 0.2f, HasWheels = true,
            Ai = EnemyAi.SideCombat, Combat = EnemyCombat.SideCannon(),
            // 直接加载 Resources 下的 glb 模型（与武器模型同一套加载方式）
            PrefabPath = "art/models/enemy/Agent_SideShooter"
        };

        // -------- 1-3 波车辆小怪：无特殊攻击，直线横冲，贴身接触伤害，攻低血薄 --------
        public static EnemyDef SupportHp() => new EnemyDef
        {
            Key = "support_hp", Hp = 50, Dmg = 2, Exp = 8, Prog = 4,
            Speed = 3.2f, Size = 1, Scale = 0.2f, HasWheels = true,
            Ai = EnemyAi.Chase, Combat = EnemyCombat.ContactVehicle(),
            PrefabPath = "art/models/enemy/Mob_SupportHp"
        };

        public static EnemyDef SupportMagnet() => new EnemyDef
        {
            Key = "support_magnet", Hp = 50, Dmg = 2, Exp = 9, Prog = 4,
            Speed = 3.0f, Size = 1, Scale = 0.2f, HasWheels = true,
            Ai = EnemyAi.Chase, Combat = EnemyCombat.ContactVehicle(),
            PrefabPath = "art/models/enemy/Mob_SupportMagnet"
        };

        // -------- 4-8 波中型炮塔车：AttackModule 朝炮（炮塔转玩家+炮管后坐），带轮子滚动 --------
        /// <summary>弓箭塔：4 个炮塔/炮口齐射箭矢</summary>
        public static EnemyDef ArcheTower() => new EnemyDef
        {
            Key = "arche_tower", Hp = 45, Dmg = 3, Exp = 20, Prog = 9,
            Speed = 1.8f, Size = 2, Scale = 0.2f, HasWheels = true,
            Ai = EnemyAi.Chase,
            Combat = EnemyCombat.Turret("Proj_Arrow", 3.2f, 14f, 11f, 3f, 0.6f, 4f),
            PrefabPath = "art/models/enemy/Mob_ArcheTower"
        };

        /// <summary>迫击炮：单发慢速重弹，射程远、间隔长</summary>
        public static EnemyDef MortarTower() => new EnemyDef
        {
            Key = "mortar_tower", Hp = 55, Dmg = 6, Exp = 24, Prog = 10,
            Speed = 1.6f, Size = 2, Scale = 0.2f, HasWheels = true,
            Ai = EnemyAi.Chase,
            Combat = EnemyCombat.Turret("Proj_CannonBall", 4f, 16f, 9f, 4f, 0.7f),
            PrefabPath = "art/models/enemy/Mob_MortarTower"
        };

        /// <summary>小炮塔：单发较快、射程近</summary>
        public static EnemyDef SmallCannon() => new EnemyDef
        {
            Key = "small_cannon", Hp = 35, Dmg = 4, Exp = 18, Prog = 9,
            Speed = 2.0f, Size = 2, Scale = 0.2f, HasWheels = true,
            Ai = EnemyAi.Chase,
            Combat = EnemyCombat.Turret("Proj_CannonBall", 2.6f, 12f, 13f, 2.5f, 0.55f),
            PrefabPath = "art/models/enemy/Mob_SmallCannon"
        };

        // ---------------- Boss 工厂（加新 Boss 就在这里再加一个，并登记进 Bosses） ----------------
        /// <summary>战争坦克 Boss（首个 Boss，骨骼动画 idle/run/attack/hit/die，贴近砸地结算）</summary>
        public static EnemyDef TankBoss() => new EnemyDef
        {
            Key = "boss_tank", IsBoss = true,
            Hp = 320, Dmg = 14, Exp = 120, Prog = 40,
            Speed = 2.6f, Size = 3, Scale = 2.0f,
            Ai = EnemyAi.Chase,
            Combat = EnemyCombat.Slam(4.5f, 6f),   // 砸地：4.5s 间隔、6m 半径
            PrefabPath = MobDir + "Enemy_Boss",
            HasAnimator = true,
            Phase2At = 0.5f, AppearTime = 0f
        };

        /// <summary>普通兵表（顺序对应 GameConfig 的 TierUnlock / MobBand 权重下标）</summary>
        public static readonly EnemyDef[] Mobs = { Sheep(), Cow(), Farmer(), Archer(), Rider(), Guncar(), SupportHp(), SupportMagnet(), ArcheTower(), MortarTower(), SmallCannon() };

        /// <summary>Boss 表（按 AppearTime 升序，BossAt 取当前时间已解锁的最后一个）</summary>
        public static readonly EnemyDef[] Bosses = { TankBoss() };

        public static EnemyDef ByKey(string key)
        {
            for (int i = 0; i < Mobs.Length; i++)
                if (Mobs[i].Key == key) return Mobs[i];
            for (int i = 0; i < Bosses.Length; i++)
                if (Bosses[i].Key == key) return Bosses[i];
            return Mobs[0];
        }

        /// <summary>敌种索引（maxTier 解锁判定用，顺序同 Mobs，未找到回退 0）</summary>
        public static int IndexOf(string key)
        {
            for (int i = 0; i < Mobs.Length; i++)
                if (Mobs[i].Key == key) return i;
            return 0;
        }

        /// <summary>t 时刻该出场的 Boss：取 AppearTime 已到达的最后一个（没配多种时恒为第一个）</summary>
        public static EnemyDef BossAt(float t)
        {
            EnemyDef pick = Bosses[0];
            for (int i = 0; i < Bosses.Length; i++)
                if (t >= Bosses[i].AppearTime) pick = Bosses[i];
            return pick;
        }
    }
}
