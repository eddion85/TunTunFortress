using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 弹体/攻击命中后的结算方式。
    /// 后续新装备需要新结算时在这里扩展（例如 Chain 链式闪电、Beam 光束、Pierce 穿透），
    /// 并在 DamageKit / AutoWeapon 里补对应分支即可，不用改选敌与开火框架。
    /// </summary>
    public enum ImpactType
    {
        /// <summary>直射单体：追到目标身上才结算，目标中途死亡则弹体消失（侧炮/弩箭）</summary>
        Direct = 0,
        /// <summary>落地范围爆炸：飞到目标地面落点后爆炸，半径内所有敌人受伤（正面炮/火箭炮）</summary>
        Area = 1,
        /// <summary>近战突刺：无弹体，瞬间判定正前方直线攻击带，自身前刺后回位，存活敌人被击退（攻城锤）</summary>
        Thrust = 2
    }

    /// <summary>飞行弹体的外观种类（决定 AutoWeapon 用哪个弹体模板，弩箭用箭矢而非炮弹）</summary>
    public enum ProjectileKind
    {
        /// <summary>圆形炮弹（默认，侧炮/正面炮/火箭炮）</summary>
        Cannon = 0,
        /// <summary>箭矢（车侧弩箭）</summary>
        Arrow = 1
    }

    /// <summary>
    /// 一件「自动攻击装备」的完整数值/表现定义（纯数据，不含逻辑）。
    /// AutoWeapon 只认这份定义选敌、开火、结算、播动画；想加新装备武器：
    /// 1) GameConfig 里加数值；2) 下面加一个返回 WeaponDef 的工厂；3) 固定炮在 BuildMounts 挂炮口，
    ///    动态挂载武器（WeaponMountSystem 换装的）由 PlayerFortress 调 AutoWeapon.RegisterMount。
    /// </summary>
    public sealed class WeaponDef
    {
        public string Id;
        public ImpactType Impact;
        /// <summary>true=手动触发（只响应 AutoWeapon.TryManual，不自动选敌开火，如攻城锤绑定冲撞按钮）</summary>
        public bool Manual;

        // —— 数值（开火时再乘 GS 的全局加成） ——
        public float Cooldown;      // 开火间隔（秒，再乘全局攻速 CdMul）
        public float MinCd;         // 两次开火之间的硬下限（秒，0=不限；攻速加成再多也不会短于它）
        public float Damage;        // 单发基础伤害
        public float ShellSpeed;    // 弹速（米/秒，弹道武器用）
        public float ShellLife;     // 弹体最长存活（秒）
        public float ImpactRadius;  // Area：爆炸半径（米）
        public float Range;         // 选敌射程（米）
        public float ArcHeight;     // 抛物线额外拱顶高度（米，0=直线下压；>0 出膛先上升再落下）

        // —— 选敌形状 ——
        public int Side;            // -1 只打左半场 / 1 只打右半场 / 0 不限
        public float Cone;          // >0 时只挑车头正前方锥形内目标（cos 阈值，越大越窄）
        public float BlindFront;    // >0 时侧炮回避正前方盲区（cos 半角阈值），把正面让给正面炮
        public bool BossFocus;      // 是否按 Survival.BossTargetEvery 隔发点名 Boss（防近身小兵抢火）

        // —— 表现 ——
        public float ShellScale;        // 弹体模型缩放
        public ProjectileKind ProjKind = ProjectileKind.Cannon; // 弹体外观（弩箭=箭矢）
        public bool FlightTrail;        // 飞行时是否持续留火焰拖尾（让弹道更明显）
        public float TrailInterval;     // 拖尾留火间隔（秒）
        public float TrailFxScale;      // 拖尾火团尺寸
        public float MuzzleFxScale;     // 出膛火光尺寸（0=不播，近战武器用）
        public float ImpactFxScale;     // 命中/爆炸特效尺寸
        public float RingFxScale;       // 冲击波环尺寸（0 = 不显示）
        public float FireShake;         // 开火震屏
        public float ImpactShake;       // 命中/爆炸震屏
        public string FireSfx;          // 开火音效（SfxKeys，空=不播）
        public string ImpactSfx;        // 命中/爆炸音效（SfxKeys，空 = 不播）
        public bool BigDamageNumber;    // 飘字是否用大号

        // —— 开火后坐动画（0 = 无后坐） ——
        public float RecoilKick;    // 开火瞬间沿武器局部 -Y 回缩距离（米）
        public float RecoilSpring;  // 回位缓动时间（秒）

        // —— 近战突刺（ImpactType.Thrust：攻城锤） ——
        public float ThrustFanTan;    // 突刺扇形半角的 tan 值（左右各该角度，45° 时为 1）
        public float ThrustMinWidth;  // 贴身最小半宽（米），避免扇形尖点在贴脸时漏判大体型
        public float ThrustMul;       // 前刺距离 = 武器自身长度 × 该值
        public float ThrustTime;      // 前刺+回位总时长（秒）
        public float KnockDecel;      // 击退减速（米/秒²，决定后退过程长短；后退距离固定为一个自身体长）

        // ---------------- 工厂：所有数值只从 GameConfig 取，禁止写死 ----------------

        /// <summary>侧方排炮（左/右各一门）：直射最近单体</summary>
        public static WeaponDef SideCannon(int side)
        {
            return new WeaponDef
            {
                Id = side < 0 ? "side_left" : "side_right",
                Impact = ImpactType.Direct,
                Cooldown = GameConfig.SideCd,
                Damage = GameConfig.SideDmg,
                ShellSpeed = GameConfig.SideShellSpeed,
                ShellLife = GameConfig.ShellLife,
                ImpactRadius = 0f,
                Range = GameConfig.WeaponRange,
                Side = side,
                Cone = 0f,
                BlindFront = GameConfig.SideBlindCone,
                BossFocus = true,
                ShellScale = GameConfig.SideShellScale,
                FlightTrail = false,
                TrailInterval = 0f,
                TrailFxScale = 0f,
                MuzzleFxScale = GameConfig.SideDmg * GameConfig.MuzzleFxPerDmg,
                ImpactFxScale = GameConfig.SideDmg * GameConfig.HitFxPerDmg,
                RingFxScale = 0f,
                FireShake = GameConfig.SideDmg * GameConfig.FireShakePerDmg,
                ImpactShake = GameConfig.SideDmg * GameConfig.ImpactShakePerDmg,
                FireSfx = SfxKeys.Cannon,
                ImpactSfx = SfxKeys.HitEnemy,
                BigDamageNumber = false
            };
        }

        /// <summary>正面直射炮（商店加装）：炮弹飞到落点地面爆炸，范围伤害</summary>
        public static WeaponDef FrontCannon()
        {
            return new WeaponDef
            {
                Id = "front_cannon",
                Impact = ImpactType.Area,
                Cooldown = GameConfig.FrontCd,
                MinCd = 0f,
                Damage = GameConfig.FrontDmg,
                ShellSpeed = GameConfig.FrontShellSpeed,
                ShellLife = GameConfig.ShellLife,
                ImpactRadius = GameConfig.FrontImpactRadius,
                Range = GameConfig.WeaponRange,
                Side = 0,
                Cone = GameConfig.FrontCone,
                BlindFront = 0f,
                BossFocus = true,
                ShellScale = GameConfig.FrontShellScale,
                FlightTrail = true,
                TrailInterval = GameConfig.FrontTrailInterval,
                TrailFxScale = GameConfig.FrontTrailFxScale,
                MuzzleFxScale = GameConfig.FrontDmg * GameConfig.MuzzleFxPerDmg,
                ImpactFxScale = GameConfig.FrontDmg * GameConfig.ExplosionFxPerDmg,
                RingFxScale = GameConfig.FrontDmg * GameConfig.RingFxPerDmg,
                FireShake = GameConfig.FrontDmg * GameConfig.FireShakePerDmg,
                ImpactShake = GameConfig.FrontDmg * GameConfig.ImpactShakePerDmg,
                FireSfx = SfxKeys.Cannon,
                ImpactSfx = SfxKeys.ExplosionBig,
                BigDamageNumber = true
            };
        }

        /// <summary>车顶火箭炮（动态挂到 Slot_Top，按等级成长）：抛物落地范围爆炸，360° 选最近敌</summary>
        public static WeaponDef Rocket(int level)
        {
            int lv = Mathf.Clamp(level, 0, 3);
            float dmg = GameConfig.RocketBaseDmg + GameConfig.RocketDmgStep * lv;
            return new WeaponDef
            {
                Id = "rocket_l" + lv,
                Impact = ImpactType.Area,
                Cooldown = Mathf.Max(GameConfig.RocketMinCd, GameConfig.RocketBaseCd - GameConfig.RocketCdStep * lv),
                MinCd = GameConfig.RocketMinCd,
                Damage = dmg,
                ShellSpeed = GameConfig.RocketShellSpeed,
                ShellLife = GameConfig.ShellLife,
                ImpactRadius = GameConfig.RocketBaseRadius + GameConfig.RocketRadiusStep * lv,
                Range = GameConfig.RocketRange,
                ArcHeight = GameConfig.RocketArcHeight,
                Side = 0,
                Cone = 0f,
                BlindFront = 0f,
                BossFocus = true,
                ShellScale = GameConfig.RocketShellScale,
                FlightTrail = true,
                TrailInterval = GameConfig.FrontTrailInterval,
                TrailFxScale = GameConfig.FrontTrailFxScale,
                MuzzleFxScale = dmg * GameConfig.MuzzleFxPerDmg,
                ImpactFxScale = dmg * GameConfig.ExplosionFxPerDmg,
                RingFxScale = dmg * GameConfig.RingFxPerDmg,
                FireShake = dmg * GameConfig.FireShakePerDmg,
                ImpactShake = dmg * GameConfig.ImpactShakePerDmg,
                FireSfx = SfxKeys.Cannon,
                ImpactSfx = SfxKeys.ExplosionBig,
                BigDamageNumber = true,
                RecoilKick = GameConfig.RocketRecoil,
                RecoilSpring = GameConfig.RocketRecoilSpring
            };
        }

        /// <summary>车侧弩箭（动态挂到 Slot_Side_Left/Right）：直射本侧半场最近单体，弹速快、弹体小</summary>
        /// <param name="side">+1 打 +X 半场（模型 Slot_Side_Left），-1 打 -X 半场（Slot_Side_Right）</param>
        public static WeaponDef Crossbow(int side)
        {
            float dmg = GameConfig.CrossbowDmg;
            return new WeaponDef
            {
                Id = side > 0 ? "crossbow_l" : "crossbow_r",
                Impact = ImpactType.Direct,
                Cooldown = GameConfig.CrossbowCd,
                MinCd = GameConfig.CrossbowMinCd,
                Damage = dmg,
                ShellSpeed = GameConfig.CrossbowShellSpeed,
                ShellLife = GameConfig.ShellLife,
                ImpactRadius = 0f,
                Range = GameConfig.CrossbowRange,
                Side = side,
                Cone = 0f,
                BlindFront = 0f,
                BossFocus = false,
                ShellScale = GameConfig.CrossbowShellScale,
                ProjKind = ProjectileKind.Arrow,
                FlightTrail = false,
                TrailInterval = 0f,
                TrailFxScale = 0f,
                MuzzleFxScale = dmg * GameConfig.MuzzleFxPerDmg,
                ImpactFxScale = dmg * GameConfig.HitFxPerDmg,
                RingFxScale = 0f,
                FireShake = 0f,
                ImpactShake = dmg * GameConfig.ImpactShakePerDmg,
                FireSfx = SfxKeys.Arrow,
                ImpactSfx = SfxKeys.HitEnemy,
                BigDamageNumber = false,
                RecoilKick = GameConfig.CrossbowRecoil,
                RecoilSpring = GameConfig.CrossbowRecoilSpring
            };
        }

        /// <summary>
        /// 车头攻城锤（动态挂到 Slot_Front，按等级成长）：正前方窄直线近战突刺，无弹体，
        /// 伤害每阶 ×1.5（L0 与火箭炮 L0 同伤），主打 Boss；一击不死的敌人沿突刺方向被击退。
        /// </summary>
        public static WeaponDef Ram(int level)
        {
            int lv = Mathf.Clamp(level, 0, 3);
            float dmg = GameConfig.RocketBaseDmg * Mathf.Pow(GameConfig.RamDmgGrow, lv);
            return new WeaponDef
            {
                Id = "ram_l" + lv,
                Impact = ImpactType.Thrust,
                Manual = true, // 只在玩家点「冲撞」按钮时突刺，不自动开火
                Cooldown = GameConfig.RamCd,
                MinCd = GameConfig.RamMinCd,
                Damage = dmg,
                ShellSpeed = 0f,
                ShellLife = 0f,
                ImpactRadius = 0f,
                Range = GameConfig.RamRange,
                Side = 0,
                Cone = 0f, // 手动触发不选敌，攻击范围由 DamageKit 的扇形参数决定
                BlindFront = 0f,
                BossFocus = true,
                ShellScale = 0f,
                FlightTrail = false,
                TrailInterval = 0f,
                TrailFxScale = 0f,
                MuzzleFxScale = 0f, // 近战不出炮口火球
                ImpactFxScale = dmg * GameConfig.HitFxPerDmg,
                RingFxScale = 0f,
                FireShake = 0f,
                ImpactShake = dmg * GameConfig.ImpactShakePerDmg,
                FireSfx = "",       // 突刺无开火音效，命中才有打击音
                ImpactSfx = SfxKeys.HitEnemy,
                BigDamageNumber = true,
                ThrustFanTan = Mathf.Tan(GameConfig.RamFanHalfAngle * Mathf.Deg2Rad),
                ThrustMinWidth = GameConfig.RamHalfWidth,
                ThrustMul = GameConfig.RamThrustMul,
                ThrustTime = GameConfig.RamThrustTime,
                // 存活敌人固定后退一个自身体长（DamageKit），这里只给后退过程的减速
                KnockDecel = GameConfig.RamKnockDecel
            };
        }
    }
}
