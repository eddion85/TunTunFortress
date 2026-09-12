using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 时间驱动的难度快照：由局内存活秒数从 GameConfig.Survival 推导，
    /// 任何系统都不缓存、不写死，需要时调用 GameConfig.ProfileAt(GS.Elapsed) 现取。
    /// </summary>
    public class DiffProfile
    {
        public float hpMul;        // 敌人血量倍率
        public float dmgMul;       // 敌人伤害倍率
        public float spdMul;       // 敌人移速倍率
        public float spawnInterval;// 当前刷怪间隔（秒）
        public int burst;          // 每次刷怪同时生成的份数
        public int maxTier;        // 当前已解锁的最强敌种索引（对应 EnemyCatalog.Mobs）
        public string phase;       // 当前节奏阶段名（HUD 显示）
    }

    /// <summary>敌种出现权重波段：到达 time 秒后采用该组权重（顺序对应 EnemyCatalog.Mobs）</summary>
    public class MobBand
    {
        public float time;
        public int[] w;
        public MobBand(float time, params int[] w) { this.time = time; this.w = w; }
    }

    /// <summary>节奏阶段名波段</summary>
    public class PhaseBand
    {
        public float time;
        public string name;
        public PhaseBand(float time, string name) { this.time = time; this.name = name; }
    }

    /// <summary>
    /// 全局数值配置（由 LayaAir 版 src/data/config.ts 1:1 移植）
    /// 数值来源：[策划书] = GDD 可追溯条目；[设计] = GDD 未记录细则、本轮自行设计
    /// </summary>
    public static class GameConfig
    {
        // ---------------- 基础 ----------------
        public const float MaxHp = 100f;              // [策划书 4.1]
        public const float Speed = 6f;                // [策划书 4.1] 6 m/s
        public const float DevourRadius = 1.5f;       // [策划书 4.1] 吞噬圈半径 1.5m
        public const float DevourTime = 6f;           // [设计] 强化吞噬生效时长
        public const float DevourBoostMul = 2.2f;     // [设计] 强化吞噬半径倍率

        /// <summary>
        /// [设计] 强化吞噬「进圈即吞」只在核心圈内生效；
        /// 核心圈内可吞目标仍秒杀，核心圈与外圈之间承受按距离递减的持续伤害（磨血 + 吸入）。
        /// </summary>
        public const float DevourCoreRatio = 0.45f;   // 秒杀核心圈占强化圈半径的比例
        public const float DevourDot = 10f;           // 强化吞噬外圈持续伤害（每秒，贴身满额）
        public const float DevourDotTick = 0.3f;      // 持续伤害结算间隔（秒）
        public const float DevourCd = 1f;             // 强化吞噬冷却（只防连点，金币才是主要门槛）
        public const int DevourCoinCost = 30;         // 强化吞噬消耗金币（广告承接点）

        /// <summary>
        /// 强化（强制）吞噬允许直接吞下的敌人 Key 白名单：只有牛/羊/farmer/rider/archer 这类小怪能被强吞；
        /// 其余敌人（车辆/炮塔/支援/Boss）在强化吞噬时只受外圈磨血与真空吸附，不会被直接吞下。
        /// </summary>
        public static readonly string[] DevourForceWhitelist = { "sheep", "cow", "farmer", "rider", "archer" };

        public const float ArenaHalf = 29f;           // 有边界时的半边长（与可见地面对齐，留 2m 视觉余量）
        public static readonly bool ArenaBounded = false;       // 是否限制活动边界：false=无边界自由移动
        public const float MiniMapViewRange = 40f;     // 无边界时小地图动态窗口半径（世界米，玩家恒在中心）
        // 无限地面：玩家开出原始地面后，循环地砖跟随平移（视觉上走不到头；纯运行时生成不改场景）
        public static readonly bool GroundFollow = true;  // 是否启用跟随地砖
        public const float GroundTileSize = 50f;          // 单块地砖边长（米）
        public const int GroundTileRadius = 2;            // 玩家周围铺 (2r+1)² 块
        public const float GroundY = -0.05f;              // 地砖高度（略低于原地面避免闪烁）

        /// <summary>按配置把世界坐标钳制在竞技场内；关闭边界时原样返回</summary>
        public static float ClampArena(float v, float expand = 0f)
        {
            if (!ArenaBounded) return v;
            float h = ArenaHalf + expand;
            return Mathf.Clamp(v, -h, h);
        }

        // ---------------- 移动冒烟拖尾 [设计]：车尾持续吐烟，独立对象池 ----------------
        public const float TrailInterval = 0.10f;       // 普通移动吐烟间隔（秒/团）
        public const float TrailDashInterval = 0.06f;   // 冲撞期间吐烟更密
        public const float TrailLife = 0.75f;           // 单团烟存活时间（秒）
        public const float TrailScale = 0.9f;           // 单团烟基础 UI 尺寸（256px 贴图 × scale × 0.75）
        public const float TrailGrow = 1.8f;            // 存活期间扩散倍率（末态 = 1 + 1.8 倍）
        public const float TrailRise = 5f;              // 屏幕上飘像素（贴地扬尘，数值很小）
        public const float TrailAlpha = 1.2f;          // 起始透明度，随后线性淡出
        public const float TrailBack = 1.2f;            // 生成点距车体中心向后的距离（米）
        public const float TrailJitter = 0.22f;         // 生成点横向随机散布（米）

        // ---------------- 地面车辙（移动时车尾左右各一道，沿真实轨迹自然弯曲） ----------------
        public const float TrackInterval = 0.05f;       // 落印间隔（秒，越小轨迹越连续）
        public const float TrackLife = 1.1f;            // 单个车辙印存活时间（秒），随后淡出
        public const float TrackScale = 0.42f;          // 车辙印基础 UI 尺寸
        public const float TrackStretch = 4.6f;         // 沿行驶方向的拉长倍数（拖成长条，明显长于冒烟团）
        public const float TrackSide = 0.42f;           // 左右车辙距车体中心的横向距离（米，随体型缩放）
        public const float TrackBack = 0.55f;           // 车辙印距车体中心向后的距离（米）
        public const float TrackAlpha = 0.5f;           // 起始不透明度
        public static readonly Color TrackColor = new Color(0.78f, 0.62f, 0.34f); // 土黄色
        public const int TrailMax = 12;                 // 同屏拖尾烟团上限（独立池，不挤占战斗特效）

        // ---------------- 武器 ----------------
        public const float SideCd = 1.4f;             // [策划书 3.5s → 压缩保手感]
        public const float SideDmg = 12f;             // [策划书 4.1]
        public const float FrontCd = 3.0f;            // [设计] 正面直射炮自动开火间隔：每 3 秒一发
        public const float FrontDmg = 18f;            // [策划书 4.1]
        public const float WeaponRange = 15f;
        public const float DashCd = 6f;               // [策划书 4.1]

        /// <summary>
        /// [设计] 侧炮正面盲区：半角的 cos 值。落在车头正前方该角度内的敌人侧炮不打，
        /// 只留给正面直射炮，避免小兵被 360° 侧炮提前清完、正面炮没有目标。
        /// cos45°≈0.707 = 盲区 ±45°（比正面炮 ±70° 射界窄，45°~70° 为双重火力带，不会有空档）；
        /// 设为 0 = 侧炮无盲区（回到 360° 覆盖），设为 1 = 正前方一条线外都不打。
        /// </summary>
        public const float SideBlindCone = 0.707f;

        // ---------------- 炮弹飞行（[设计] 抽成配置，供所有装备武器复用） ----------------
        public const float SideShellSpeed = 20f;       // 侧炮弹速（米/秒）
        public const float FrontShellSpeed = 26f;      // 正面炮弹速
        public const float ShellLife = 1.6f;           // 炮弹最长存活（秒），到期按当前位置结算
        public const float ShellArriveDist = 0.9f;     // 距落点多少米判定命中/爆炸
        public const float SideShellScale = 0.3f;      // 侧炮弹体世界直径（米，danyao1 球形）
        public const float FrontShellScale = 1.5f;     // 正面炮弹体模型缩放（更大更醒目）
        public const float FrontTrailInterval = 0.05f; // 正面炮飞行拖尾：每多少秒留一团火
        public const float FrontTrailFxScale = 0.85f;  // 拖尾火团尺寸（收小，避免糊屏）

        // ---------------- 正面直射炮：落地范围爆炸（AoE） ----------------
        public const float FrontImpactRadius = 3.2f;   // 落地爆炸半径（米），半径内全部敌人受伤

        // 攻击特效尺寸/震屏统一按武器基础伤害换算（威力越大特效越大），整体收小避免炸屏：
        // 侧炮伤害12、正面炮18、地雷21.6，自然形成「侧炮 &lt; 正面炮 &lt; 地雷」的表现梯度
        public const float MuzzleFxPerDmg = 0.07f;     // 出膛火光尺寸 = 伤害 × 系数
        public const float HitFxPerDmg = 0.085f;       // 单体命中火花尺寸
        public const float ExplosionFxPerDmg = 0.12f;  // 范围爆炸序列帧尺寸
        public const float RingFxPerDmg = 0.09f;       // 冲击波环尺寸
        public const float FireShakePerDmg = 0.008f;   // 开火震屏
        public const float ImpactShakePerDmg = 0.02f;  // 命中/爆炸震屏

        // ---------------- 正面炮选敌射界 ----------------
        public const float FrontCone = 0.35f;       // 正面炮只打车头正前方锥形（cos 阈值，约 ±70°）

        // ---------------- 车顶火箭炮（Slot_Top，4 级，抛物落地范围爆炸） ----------------
        public const float RocketRange = 16f;           // 选敌射程（米）
        public const float RocketBaseCd = 3.2f;         // L0 开火间隔（秒），每升一级减 RocketCdStep
        public const float RocketCdStep = 0.3f;
        public const float RocketMinCd = 2.0f;       // 两次发射硬下限（攻速加成再多也不短于 2s）
        public const float RocketBaseDmg = 16f;         // L0 单发基础伤害，每升一级 +RocketDmgStep
        public const float RocketDmgStep = 8f;
        public const float RocketBaseRadius = 2.6f;     // L0 爆炸半径，每升一级 +RocketRadiusStep
        public const float RocketRadiusStep = 0.3f;
        public const float RocketShellSpeed = 10f;      // 弹速（米/秒）
        public const float RocketArcHeight = 6.5f;      // 抛物线拱顶额外高度（出膛先升后落，0=直线下压）
        public const float RocketShellScale = 1.2f;     // 弹体模型缩放
        public const float RocketRecoil = 0.12f;        // 开火后坐位移（武器局部 Y，米）
        public const float RocketRecoilSpring = 0.14f;  // 后坐回位缓动时间（秒，越小回弹越快）

        // ---------------- 车侧弩箭（Slot_Side_Left/Right，直射单体） ----------------
        public const float CrossbowRange = 15f;         // 选敌射程（米）
        public const float CrossbowCd = 1.8f;           // 每把弩开火间隔（秒）
        public const float CrossbowMinCd = 1.2f;        // 两次发射硬下限（秒）
        public const float CrossbowDmg = 9f;            // 每支弩箭基础伤害
        public const float CrossbowShellSpeed = 26f;    // 弩箭弹速（米/秒，快于炮弹）
        public const float CrossbowShellScale = 0.5f;   // 弹体缩放（小而快）
        public const float CrossbowRecoil = 0.08f;      // 开火后坐位移（武器局部 -Y，即朝车内回缩）
        public const float CrossbowRecoilSpring = 0.1f;

        // ---------------- 车头攻城锤（Slot_Front，4 阶，直线近战突刺，主打 Boss） ----------------
        public const float RamRange = 12f;               // 攻击距离 = 火箭炮射程一半（米）
        public const float RamDmgGrow = 1.5f;           // 每升一阶伤害 ×1.5（L0 与火箭炮 L0 同伤）
        public const float RamCd = 2.2f;                // 突刺间隔（秒）
        public const float RamMinCd = 1.6f;             // 突刺硬下限（秒）
        public const float RamFanHalfAngle = 45f;       // 突刺扇形：车头左右各 45°（总张角 90°）
        public const float RamHalfWidth = 1.6f;         // 贴身最小半宽（米）：防止贴脸时扇形尖点漏判大体型
        public const float EquipSizeRatio = 1f / 3f;    // 所有装配武器统一为「当前车体」对应轴长度的 1/3
        public const float RamLengthRatio = EquipSizeRatio; // 兼容旧名（攻城锤长度比例）
        public const float RamThickMul = 2.0f;          // 粗细倍率：只加粗 X/Z（垂直长轴方向），长度不变
        public const float RamThrustMul = 0.8f;        // 突刺前冲距离 = 自身长度 × 该值（要求 >1/2）
        public const float RamThrustTime = 0.28f;       // 一次前刺+回位总时长（秒）
        public const float RamKnockDecel = 10f;         // 击退减速（米/秒²，越大回位越快）
        public const float RamKnockMoveSuppress = 0f;   // 被击退期间自身追击完全停住（0=原地被顶退，不往前挪）
        public static readonly Vector3 RamLocalEuler = new Vector3(90f, 0f, 0f); // 模型长轴 +Y 转到车头 +Z

        // ---------------- 玩家冲撞 ----------------
        public const float DashTime = 0.34f;        // 冲撞持续秒数
        public const float DashSpeedMul = 3.1f;     // 冲撞速度倍率
        public const float DashFxInterval = 0.13f;  // 冲撞期间速度线特效间隔
        public const float DashHitRadius = 2.4f;    // 冲撞碾压半径（米）
        public const float DashDmgMul = 1.5f;       // 冲撞伤害 = 侧炮伤害 × 该倍率
        public const float EvolveFxInterval = 0.18f;// 进化金光期间持续特效间隔

        // ---------------- 敌人 AI / 刷怪 / 击杀表现（原 EnemySpawner 内字面量集中配置） ----------------
        public static class Ai
        {
            // 出生落位：玩家外圈环形带
            public const float SpawnRingMin = 15f;      // 普通兵出生最小距离（米）
            public const float SpawnRingJitter = 8f;    // 额外随机距离
            public const float BossSpawnRing = 18f;     // Boss 出生距离
            public const float BossPhase2Mul = 1.4f;    // Boss 血量过半后移速倍率
            public const float BossDieAnimTime = 1.6f;  // Boss 死亡动画时长
            public const float BossHitAnimTime = 0.4f;  // Boss 受击动画时长
            public const float BossAttackAnimTime = 0.7f;

            // 弓箭手：保持射程带、远程射箭
            public const float ArcherKeepMin = 8f;      // 小于该距离后退
            public const float ArcherKeepMax = 11f;     // 大于该距离前进
            public const float ArcherShootRange = 14f;  // 开火最大距离
            public const float ArcherShootCd = 2.2f;    // 射击间隔
            public const float ArrowSpeed = 14f;        // 箭飞行速度
            public const float ArrowLife = 2.5f;        // 箭最长存活
            public const float ArrowHitRadius = 1.2f;   // 箭命中玩家半径

            // 敌弹体积随伤害放大：倍率 = clamp(伤害 / 参考伤害, 最小, 最大)，伤害越大弹越大
            public const float ProjSizeRefDmg = 6f;     // 该伤害下倍率=1（即 ProjScale 标称尺寸）
            public const float ProjSizeMin = 0.6f;      // 体积倍率下限
            public const float ProjSizeMax = 3f;        // 体积倍率上限

// 侧炮车（Agent_SideShooter：AttackModules 下左右各 4 个炮塔）
            public const float EnemySideCannonCd = 2.8f;        // 齐射间隔（秒）
            public const float EnemySideCannonRange = 13f;      // 开火最大距离
            public const float EnemySideCannonSpread = 8f;      // 炮口散射总夹角（度）
            public const float EnemySideCannonProjSpeed = 12f;  // 炮弹速度
            public const float EnemySideCannonProjLife = 3f;    // 炮弹存活
            public const float EnemySideCannonProjScale = 0.55f;// 旧炮弹缩放（已弃用，保留兼容）
            public const float SideShellDiam = 0.7f;            // 侧炮 danyao1 弹体在参考伤害下的世界直径（米）
            public const float EnemySideCannonMuzzleHeight = 1f;// 无炮口节点时的发射高度
            public const string SideShooterMuzzleNode = "AttackInstantiationPoint"; // 炮口节点名（同名全部收集）
            public const string SideShooterTurretNode = "AttackModule_(Turret)";    // 转向玩家的炮塔节点名
            public const string SideShooterBarrelNode = "Cannon_Holder";            // 后坐炮管节点名
            public const float SideShooterRecoilDist = 0.25f;  // 开火后坐距离（米，沿炮管本地 -Z）
            public const float SideShooterRecoilTime = 0.22f;  // 后坐回位时长（秒）
            // 侧炮车走位（EnemyAi.SideCombat：绕到玩家并排侧位再开火）
            public const float SideShooterKeepDist = 7f;    // 与玩家保持的并排距离（米）
            public const float SideShooterArriveDist = 1.6f;// 进入侧位多少米内视为到位（减速停住）
            public const float SideShooterHoldSpeedMul = 0.15f; // 到位后移速倍率（几乎停住横在玩家侧面）
            public const float SideShooterFlipEvery = 7f;   // 多少秒切换一次左右侧（0=不换）
            public const float SideShooterBroadside = 40f;  // 车身与玩家夹角偏离正侧位不超过该角度才开火
            // 通用炮塔（AttackModule 朝炮：迫击炮/小炮塔/弓箭塔）
            public const string EnemyMuzzleNode = "AttackInstantiationPoint"; // 炮口节点名（同名全部收集）
            public const string EnemyTurretNode = "AttackModule_(Turret)";    // 转向玩家的炮塔节点名
            public const string EnemyBarrelNode = "Cannon_Holder";           // 后坐炮管节点名（弓箭塔没有就找不到，自动空转）
            public const float EnemyTurretMuzzleHeight = 1f;
            public const float EnemyTurretRecoilDist = 0.22f;
            public const float EnemyTurretRecoilTime = 0.2f;
            // Boss 迫击炮 T2：固定多联装炮管，炮口节点名 InstantiationPoint（不转向、无后坐炮管）
            public const string BossMortarMuzzleNode = "InstantiationPoint";

            // 轮子滚动（EnemyWheelRig）
            public const string EnemyWheelsNode = "Wheels";  // 车轮根节点名
            public const int EnemyWheelSpinSign = 1;         // 滚动方向反了就改成 -1
            public const string EnemyBarNode = "HPBar_Bar";  // 模型自带血条锚点（血条贴这里显示）
            // 轮式敌人移动时的车轮轨迹（复用玩家车辙印 Fx.PlayTrackMark）
            public static readonly bool EnemyTrackEnabled = true;  // 是否留车轮轨迹
            public const float EnemyTrackDist = 1.0f;    // 每走多少米落一组印（越小越连续，怪多时别太小避免压 UI 池）
            public const float EnemyTrackScale = 0.8f;  // 单印 UI 尺寸
            public const float EnemyTrackStretch = 8.0f; // 沿行驶方向拉长倍数
            public const float EnemyTrackY = 0.08f;      // 贴地高度

            // 骑兵绕侧
            public const float FlankBreakDist = 6f;     // 小于该距离改为直冲
            public const float FlankBlendDist = 14f;    // 绕侧分量随距离拉满的距离
            public const float FlankForward = 0.75f;    // 前冲分量占比

            // 羊：近逃远追中游荡
            public const float WanderFleeDist = 4f;
            public const float WanderGatherDist = 9f;
            public const float FleeSpeedMul = 1.1f;
            public const float GatherSpeedMul = 0.85f;
            public const float WanderSpeedMul = 0.6f;
            public const float WanderIntervalMin = 1.2f;
            public const float WanderIntervalRand = 1.5f;

            // 近身攻击
            public const float MeleeHitRadius = 1.9f;       // 普通兵贴身伤害半径
            public const float MeleeHitRadiusBoss = 3.2f;  // Boss 贴身伤害半径
            public const float MeleeHitCd = 1f;            // 贴身伤害间隔

            // 敌人之间防重叠（小兵/Boss 绝不互相穿插）：两两检测到太近时，随机让其中一个原地静止一会儿、另一个继续走
            public static readonly bool EnemyYieldEnabled = true;
            public const float EnemySepGap = 1.02f;       // “太近”距离 = 双方半径和 × 该系数（略大于 1 留缝）
            public const float EnemyYieldFreezeSec = 1f;  // 被选中让行的一方静止时长（秒）

            // 强化吞噬真空吸附
            public const float DevourPullRadiusMul = 2.4f; // 吸附半径 = 吞噬圈 × 该值
            public const float DevourPullFar = 9f;         // 外圈吸附强度
            public const float DevourPullNear = 3f;        // 贴脸额外吸附
            public const float DevourDotEdge = 0.15f;      // 外圈边缘伤害比例
            public const float DevourDotPow = 1.3f;        // 外圈伤害衰减曲线幂

            // 击杀/受击表现尺寸与震屏
            public const float MobDeathFx = 1.8f;
            public const float BossDeathFx = 5f;
            public const float BossDeathRingFx = 5f;
            public const float BossDeathSparkFx = 3f;
            public const float MobDeathShake = 0.22f;
            public const float BossDeathShake = 1.8f;
            public const float BossSlamRingFx = 4.5f;
            public const float BossSlamExplosionFx = 4f;
            public const float BossSlamShake = 1.4f;
            public const float ArrowHitFx = 1.6f;
            public const float ArrowHitShake = 0.4f;
            public const float MeleeHitFx = 1.8f;
            public const float MeleeHitShake = 0.45f;
            public const float BossAppearRingFx = 7f;
            public const float BossAppearBeamFx = 5.5f;
            public const float BossAppearShake = 2.4f;
        }

        // ---------------- 副武器：连枷（SubWeapons） ----------------
        public static class Flail
        {
            public const int Count = 2;             // 连枷数量
            public const float RotateSpeed = 3.4f;  // 环绕角速度（弧度/秒）
            public const float Radius = 2.6f;       // 环绕半径（米）
            public const float Height = 0.8f;       // 离地高度
            public const float HitRadius = 1.1f;    // 碰到敌人的判定半径
            public const float DmgMul = 0.85f;      // 单次伤害 = 侧炮伤害 × 该值
            public const float HitCd = 0.35f;       // 同一连枷两次命中间隔
            public const float Scale = 1.5f;        // 模型缩放
        }

        // ---------------- 副武器：地雷舱（SubWeapons） ----------------
        public static class Mine
        {
            public const int Max = 10;              // 同屏地雷上限
            public const float Life = 14f;          // 地雷存活秒数
            public const float Arm = 0.6f;          // 布雷后武装延迟
            public const float StepDist = 2.2f;     // 每移动多少米可布一颗
            public const float Cd = 1.6f;           // 布雷间隔
            public const float TriggerRadius = 1.4f;// 敌人靠近引爆半径
            public const float Scale = 1.1f;        // 模型缩放
            public const float ExplosionRadius = 3f;// 爆炸半径
            public const float DmgMul = 1.2f;       // 爆炸伤害 = 正面炮 × 该值
        }

        // ---------------- 掉落物（DropSystem / DropItem） ----------------
        public static class Drop
        {
            public const float HealthP = 0.08f;     // 小血包概率
            public const float MagnetP = 0.12f;     // 磁铁累计概率阈值
            public const float CoinP = 0.55f;       // 金币累计概率阈值（之后为空手）
            public const float MagnetSpeed = 12f;   // 磁吸飞行速度
            public const float PickRadiusBonus = 1.6f; // 拾取半径 = 吞噬圈 + 该值
            public const float BobAmp = 0.25f;      // 上下浮动幅度
            public const float BobSpeed = 3f;       // 上下浮动速度
            public const float Spin = 2.4f;         // 自转速度
            public const float CoinExp = 4f;        // 拾取金币附带经验
            public const float ScaleSmall = 1.25f;  // 普通掉落缩放
            public const float ScaleBig = 1.4f;     // Boss 掉落缩放
            public const float Life = 18f;          // 掉落物存活秒数
            public const float SpawnY = 0.7f;       // 出生高度
            public const float FloatY = 0.85f;      // 浮动中心高度
        }

        // ---------------- 进化 ----------------
        public static readonly int[] EvolveLevels = { 4, 6, 8 };        // [策划书 4.1]
        // 每进阶 1 级模型放大 1.5 倍：1 / 1.5 / 2.25 / 3.375
        public static readonly float[] StageScale = { 1f, 1.5f, 2.25f, 3.375f };
        public static readonly float[] StageHp = { 1f, 1.4f, 1.9f, 2.5f };
        public const float EvolveShowTime = 1.5f;     // [策划书] 金光变身 1.5s

        // ---------------- 体积压制 [策划书 4.1] ----------------
        public const float SizeBonusPerTier = 0.1f;   // 大欺小每级 +10%
        public const float SizeBonusCap = 0.5f;       // 上限 +50%
        public const float SizeWeakFactor = 0.7f;     // 小打大 0.7

        public const float ComboWindow = 2.5f;        // [设计] 连击窗口秒数
        public const int MaxUnits = 25;               // [美术书 2.2] 同屏单位总上限（兜底保险）

        /// <summary>每帧 dt 上限，防止切后台回来后一帧跳变</summary>
        public const float MaxDelta = 0.05f;

        // ====================================================================
        //  无尽生存：难度完全由「局内存活时间」驱动，所有数值集中在此配置，
        //  刷怪 / Boss / 复活逻辑只读取这里，禁止在业务代码里写死曲线。
        // ====================================================================
        public static class Survival
        {
            // ---------- 刷怪节奏 ----------
            public const float SpawnIntervalStart = 1.5f;   // 开局刷怪间隔（秒/只）
            public const float SpawnIntervalMin = 0.42f;    // 刷怪间隔下限，再快也不超过它
            public const float SpawnRampTime = 200f;        // 经过多少秒线性压缩到下限
            public const float SpawnFirstDelay = 0.6f;      // 开局第一只怪延迟
            public const float BurstEvery = 35f;            // 每存活 N 秒，每波多刷 1 只
            public const int BurstCap = 4;                  // 每波份数上限
            public const int MobCap = 22;                   // 同屏普通兵上限（不含 Boss，达到就暂停刷小怪）
            // 滚动窗口限流：任意 SpawnWindowSeconds 秒内新刷出的普通兵不超过 SpawnPerWindowCap，
            // 把小兵数量压住；后期难度改由 Boss 的数量与血量承担（见 BossCountStep / BossGrow）
            public const float SpawnWindowSeconds = 30f;    // 刷怪数量统计窗口（秒）
            public const int SpawnPerWindowCap = 16;        // 窗口内普通兵出生数量上限

            // ---------- 敌人成长：每 StepSeconds 秒乘一次 Grow（指数曲线）----------
            public const float StepSeconds = 30f;
            public const float HpGrow = 1.16f;              // 血量每 30s ×1.16
            public const float DmgGrow = 1.09f;             // 伤害每 30s ×1.09
            public const float SpdGrow = 1.02f;             // 小兵移速每 30s ×1.02（放缓，避免后期跑不过来）
            public const float SpdCap = 1.2f;               // 小兵移速倍率上限

            // ---------- 敌种按存活时间解锁（下标对应 EnemyCatalog.Mobs：羊/牛/农夫/弓/骑）----------
            public static readonly float[] TierUnlock = { 0f, 12f, 30f, 55f, 65f, 90f, 0f, 8f, 45f, 60f, 72f }; // 6/7=车辆小怪 8/9/10=中型炮塔

            // ---------- 敌种出现权重波段（到达对应秒数后切换，未解锁的种自动屏蔽）----------
            public static readonly MobBand[] MobTable =
            {
                new MobBand(0f,   10, 0, 0, 0, 0, 0, 5, 4, 0, 0, 0),
                new MobBand(12f,  6, 4, 0, 0, 0, 0, 4, 3, 0, 0, 0),
                new MobBand(30f,  4, 3, 3, 0, 0, 0, 3, 2, 0, 0, 0),
                new MobBand(55f,  3, 2, 3, 2, 0, 0, 2, 2, 0, 1, 1),
                new MobBand(85f,  2, 2, 2, 2, 2, 0, 1, 1, 2, 1, 2),
                new MobBand(130f, 2, 1, 2, 2, 3, 2, 1, 1, 2, 2, 2),
                new MobBand(190f, 1, 1, 2, 2, 4, 3, 1, 1, 2, 2, 3),
            };

            // ---------- HUD 节奏阶段名 ----------
            public static readonly PhaseBand[] Phases =
            {
                new PhaseBand(0f,   "农场草原 · 初醒"),
                new PhaseBand(45f,  "麦浪起 · 潮涌"),
                new PhaseBand(90f,  "铁蹄逼近 · 围攻"),
                new PhaseBand(150f, "杀红了眼 · 狂潮"),
                new PhaseBand(220f, "无尽炼狱"),
            };

            // ---------- Boss 时间表 ----------
            public const float BossFirstTime = 90f;     // 第一只 Boss 出场秒数
            public const float BossAppearT0 = 150f;     // 钻车 Boss（T0）从第几秒起可能出场
            public const float BossAppearT2 = 300f;     // 迫击炮 Boss（T2）解锁秒数
            public const float BossAppearT3 = 480f;     // 双管炮 Boss（T3）解锁秒数
            public const float BossInterval = 70f;      // 上一只 Boss 死亡后，隔多少秒再来一只
            public const float BossBatchGap = 12f;      // 同批次补多只 Boss 时的出场间隔（秒）
            public const float BossCountStep = 120f;    // 首 Boss 后每存活 N 秒，同屏 Boss 数量 +1（后期难度来源）
            public const int BossCountMax = 3;          // 同屏 Boss 数量上限
            public const float BossGrowEvery = 75f;     // Boss 每存活到 N 秒变强一档
            public const float BossGrow = 1.3f;         // Boss 每档血量倍率
            public const int BossTargetEvery = 2;       // 武器每 N 发强制点名最近的 Boss，防止近身小兵永久挡火导致 Boss 不掉血（1=每发都优先）
            public const int BossCoin = 40;             // 击杀 Boss 额外金币
            public const int KillCoinBase = 3;          // 每杀一只普通兵基础金币（另加敌种 prog 值）

            // ---------- 死亡复活 ----------
            public const int ReviveMax = 1;             // 每局最多复活次数
            public const int ReviveCoinCost = 0;        // 每次复活消耗的局内金币（0=免费）
            public const float ReviveHpRatio = 0.6f;    // 复活后恢复到最大生命的比例
            public const float ReviveInvincible = 3f;   // 复活后无敌秒数
            public const bool ReviveClearMobs = true;   // 复活时是否清空场上普通兵（Boss 保留）
            public const float LoseCoinRatio = 0.3f;    // 最终放弃时，局内金币按此比例转入局外存档

            // ---------- 击杀里程碑：系统自动弹出三选一（不允许局内手动购买装备） ----------
            public const int OfferKillsFirst = 12;     // 第一次奖励所需击杀数
            public const int OfferKillsGrow = 6;       // 之后每次阈值递增：第 n 次需要 First + n*Grow 杀
            public const int OfferMaxPicks = 3;        // 每次里程碑最多可选择的次数（每次选择后重新过滤，必须仍满足条件）
            public const float OfferHealBelowRatio = 0.92f; // 生命高于最大生命该比例时回复卡不出现（保证弹的都是真正可用）
            public const int OfferCostWeapon = 60;    // 解锁一把武器的金币售价
            public const int OfferCostUpgrade = 40;   // 武器升星一次的金币售价
            public const int OfferCostBuff = 25;      // 数值词条（伤害/攻速/生命等）售价
            public const int OfferCostHeal = 20;      // 紧急修复售价
        }

        /// <summary>按存活秒数推导当前难度快照（时间驱动，替代旧的按关卡推导）</summary>
        public static DiffProfile ProfileAt(float t)
        {
            if (t < 0f) t = 0f;
            float step = t / Survival.StepSeconds;

            // 刷怪间隔：开局值线性压缩到下限
            float k = Mathf.Clamp01(t / Survival.SpawnRampTime);
            float interval = Mathf.Lerp(Survival.SpawnIntervalStart, Survival.SpawnIntervalMin, k);

            // 每波份数：每 BurstEvery 秒 +1，封顶
            int burst = Mathf.Min(Survival.BurstCap, 1 + Mathf.FloorToInt(t / Survival.BurstEvery));

            // 敌种解锁档位：最后一个已到达解锁时间的种
            int tier = 0;
            for (int i = 0; i < Survival.TierUnlock.Length; i++)
                if (t >= Survival.TierUnlock[i]) tier = i;

            // 阶段名
            string phase = Survival.Phases[0].name;
            for (int i = 0; i < Survival.Phases.Length; i++)
                if (t >= Survival.Phases[i].time) phase = Survival.Phases[i].name;

            return new DiffProfile
            {
                hpMul = Mathf.Pow(Survival.HpGrow, step),
                dmgMul = Mathf.Pow(Survival.DmgGrow, step),
                spdMul = Mathf.Min(Survival.SpdCap, Mathf.Pow(Survival.SpdGrow, step)),
                spawnInterval = interval,
                burst = burst,
                maxTier = tier,
                phase = phase
            };
        }

        /// <summary>取 t 时刻的敌种权重波段（最后一个 time&lt;=t）</summary>
        public static MobBand MobBandAt(float t)
        {
            MobBand band = Survival.MobTable[0];
            for (int i = 0; i < Survival.MobTable.Length; i++)
                if (t >= Survival.MobTable[i].time) band = Survival.MobTable[i];
            return band;
        }

        /// <summary>Boss 在 t 出场时的额外血量倍率（随存活时间成长）</summary>
        public static float BossHpMulAt(float t)
        {
            return Mathf.Pow(Survival.BossGrow, Mathf.Floor(t / Survival.BossGrowEvery));
        }

        /// <summary>t 时刻允许同屏存在的 Boss 数量：首 Boss 1 只，之后每 BossCountStep 秒 +1，封顶</summary>
        public static int BossCountAt(float t)
        {
            if (t < Survival.BossFirstTime) return 0;
            int n = 1 + Mathf.FloorToInt((t - Survival.BossFirstTime) / Survival.BossCountStep);
            return Mathf.Clamp(n, 1, Survival.BossCountMax);
        }
    }
}
