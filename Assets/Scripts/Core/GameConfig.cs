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
        public int maxTier;        // 当前已解锁的最强敌种索引（对应 EnemyDefs.KINDS）
        public string phase;       // 当前节奏阶段名（HUD 显示）
    }

    /// <summary>敌种出现权重波段：到达 time 秒后采用该组权重（顺序对应 EnemyDefs.KINDS）</summary>
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

        public const float ArenaHalf = 29f;           // 与可见地面对齐，留 2m 视觉余量

        // ---------------- 武器 ----------------
        public const float SideCd = 1.4f;             // [策划书 3.5s → 压缩保手感]
        public const float SideDmg = 12f;             // [策划书 4.1]
        public const float FrontCd = 1.0f;            // [策划书 2.5s → 同比压缩]
        public const float FrontDmg = 18f;            // [策划书 4.1]
        public const float WeaponRange = 15f;
        public const float DashCd = 6f;               // [策划书 4.1]

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

            // ---------- 敌种按存活时间解锁（下标对应 EnemyDefs.KINDS：羊/牛/农夫/弓/骑）----------
            public static readonly float[] TierUnlock = { 0f, 12f, 30f, 55f, 85f };

            // ---------- 敌种出现权重波段（到达对应秒数后切换，未解锁的种自动屏蔽）----------
            public static readonly MobBand[] MobTable =
            {
                new MobBand(0f,   10, 0, 0, 0, 0),
                new MobBand(12f,  6, 4, 0, 0, 0),
                new MobBand(30f,  4, 3, 3, 0, 0),
                new MobBand(55f,  3, 2, 3, 2, 0),
                new MobBand(85f,  2, 2, 2, 2, 2),
                new MobBand(130f, 2, 1, 2, 2, 3),
                new MobBand(190f, 1, 1, 2, 2, 4),
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
