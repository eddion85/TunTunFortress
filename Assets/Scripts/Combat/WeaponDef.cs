using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 弹体命中后的结算方式。
    /// 后续新装备需要新结算时在这里扩展（例如 Chain 链式闪电、Beam 光束、Pierce 穿透），
    /// 并在 DamageKit / AutoWeapon 的弹体推进里补对应分支即可，不用改选敌与开火框架。
    /// </summary>
    public enum ImpactType
    {
        /// <summary>直射单体：追到目标身上才结算，目标中途死亡则弹体消失（侧炮）</summary>
        Direct = 0,
        /// <summary>落地范围爆炸：飞到目标地面落点后爆炸，半径内所有敌人受伤（正面直射炮）</summary>
        Area = 1
    }

    /// <summary>
    /// 一件「自动攻击装备」的完整数值/表现定义（纯数据，不含逻辑）。
    /// AutoWeapon 只认这份定义开火、飞弹、结算；想加新装备武器：
    /// 1) GameConfig 里加数值；2) 下面加一个返回 WeaponDef 的工厂；3) AutoWeapon.BuildMounts 里挂一个炮口。
    /// </summary>
    public sealed class WeaponDef
    {
        public string Id;
        public ImpactType Impact;

        // —— 数值（开火时再乘 GS 的全局加成） ——
        public float Cooldown;      // 开火间隔（秒）
        public float Damage;        // 单发基础伤害
        public float ShellSpeed;    // 弹速（米/秒）
        public float ShellLife;     // 弹体最长存活（秒）
        public float ImpactRadius;  // Area：爆炸半径（米）

        // —— 选敌形状 ——
        public int Side;            // -1 只打左半场 / 1 只打右半场 / 0 不限
        public float Cone;          // >0 时只挑车头正前方锥形内目标（cos 阈值，越大越窄）
        public float BlindFront;    // >0 时侧炮回避正前方盲区（cos 半角阈值），把正面让给正面炮
        public bool BossFocus;      // 是否按 Survival.BossTargetEvery 隔发点名 Boss（防近身小兵抢火）

        // —— 表现 ——
        public float ShellScale;        // 弹体模型缩放
        public bool FlightTrail;        // 飞行时是否持续留火焰拖尾（让弹道更明显）
        public float TrailInterval;     // 拖尾留火间隔（秒）
        public float TrailFxScale;      // 拖尾火团尺寸
        public float MuzzleFxScale;     // 出膛火光尺寸
        public float ImpactFxScale;     // 命中/爆炸特效尺寸
        public float RingFxScale;       // 冲击波环尺寸（0 = 不显示）
        public float FireShake;         // 开火震屏
        public float ImpactShake;       // 命中/爆炸震屏
        public string FireSfx;          // 开火音效（SfxKeys）
        public string ImpactSfx;        // 命中/爆炸音效（SfxKeys，空 = 不播）
        public bool BigDamageNumber;    // 飘字是否用大号

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
                Damage = GameConfig.FrontDmg,
                ShellSpeed = GameConfig.FrontShellSpeed,
                ShellLife = GameConfig.ShellLife,
                ImpactRadius = GameConfig.FrontImpactRadius,
                Side = 0,
                Cone = 0.35f,
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
    }
}
