using System;
using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 局内状态机（对应 LayaAir 版 GS）。
    /// 所有局内数值都在这里，任何系统都只读/调用它，不各自维护副本。
    /// </summary>
    public static class GS
    {
        // ---------------- 基础数值 ----------------
        public static float Hp = GameConfig.MaxHp;
        public static float MaxHp = GameConfig.MaxHp;
        public static int Level = 1;
        public static float Exp = 0f;
        public static int ExpNeed = 30;
        public static int Kills = 0;
        public static int Coins = 0;
        public static int Stage = 0;

        // ---------------- 词条倍率 ----------------
        public static float SpeedMul = 1f;
        public static float DevourMul = 1f;
        public static float DmgMul = 1f;
        public static float CdMul = 1f;
        public static float ExpMul = 1f;

        // ---------------- 流程标志 ----------------
        public static bool Paused = false;
        public static bool Over = false;
        public static float Elapsed = 0f;
        public static bool Invincible = false;

        // ---------------- 连击 ----------------
        public static int Combo = 0;
        public static float ComboTimer = 0f;
        public static int BestCombo = 0;

        // ---------------- 道具 / 武器解锁 ----------------
        public static float MagnetTimer = 0f;
        public static bool HasFrontCannon = false;
        public static bool HasFlail = false;   // 连枷：环绕近身武器
        public static bool HasMine = false;    // 地雷舱：行进中留雷
        public static bool HasMagnet = false;  // 磁力线圈（堡垒外观）

        /// <summary>三选一/商店累积的最大生命加成，进化重算时必须并入</summary>
        public static float HpBonusMul = 1f;

        /// <summary>待使用的强化点数：升级累积，由玩家自己决定何时开面板消费</summary>
        public static int UpgradePoints = 0;

        // ---------------- 无尽生存：复活 / Boss 调度 ----------------
        /// <summary>本局已使用的复活次数</summary>
        public static int ReviveUsed = 0;
        /// <summary>复活后的无敌剩余秒数</summary>
        public static float ReviveInvincibleT = 0f;
        /// <summary>下一只 Boss 计划出场的存活秒数（由 EnemySpawner 维护，HUD 读）</summary>
        public static float NextBossTime = GameConfig.Survival.BossFirstTime;
        /// <summary>当前 Boss 计时窗口的起点（首次为 0，之后为上一只 Boss 的死亡时刻）</summary>
        public static float BossWindowStart = 0f;
        /// <summary>本局金币是否已结算进局外存档（防止复活后重复结算）</summary>
        public static bool Settled = false;

        /// <summary>强化吞噬剩余时间（秒）：圈变大且可吞高一档体积；基础吞噬始终可用</summary>
        public static float DevourTimer = 0f;
        public static float DevourCd = 0f;

        public static bool BossAlive = false;

        // ---------------- 查询 ----------------
        /// <summary>当前时间点的难度快照（时间驱动）</summary>
        public static DiffProfile Profile() { return GameConfig.ProfileAt(Elapsed); }

        /// <summary>
        /// 重置局内状态：无尽生存只有「重新开局」，没有下一关。
        /// </summary>
        public static void Reset()
        {
            // 局外养成加成在开局注入
            MaxHp = Mathf.Round(GameConfig.MaxHp * Meta.HpMul());
            Hp = MaxHp;
            Level = 1;
            Exp = 0f;
            ExpNeed = NeedFor(1);
            Kills = 0;
            Coins = 0;
            Stage = 0;
            SpeedMul = 1f;
            DevourMul = Meta.DevourMul();
            DmgMul = Meta.AtkMul();
            CdMul = 1f;
            ExpMul = 1f;
            BestCombo = 0;
            HasFrontCannon = false;
            HasFlail = false;
            HasMine = false;
            HasMagnet = false;
            HpBonusMul = 1f;
            UpgradePoints = 0;

            Paused = false;
            Over = false;
            Elapsed = 0f;
            Combo = 0;
            ComboTimer = 0f;
            MagnetTimer = 0f;
            BossAlive = false;
            DevourTimer = 0f;
            DevourCd = 0f;
            Invincible = false;

            ReviveUsed = 0;
            ReviveInvincibleT = 0f;
            NextBossTime = GameConfig.Survival.BossFirstTime;
            BossWindowStart = 0f;
            Settled = false;
        }

        /// <summary>
        /// 经验曲线 [策划书 4.2 曲线 + 设计修正]。
        /// 指数曲线：前期保证 Lv4/6/8 三次进化在首关内可达，之后成本快速拉开。
        /// </summary>
        public static int NeedFor(int level)
        {
            return Mathf.RoundToInt(18f * Mathf.Pow(1.34f, level - 1) + 12f);
        }

        /// <summary>强化吞噬是否生效（不影响基础吞噬能力）</summary>
        public static bool DevourBoosted() { return DevourTimer > 0f; }

        /// <summary>
        /// 开启强化吞噬：必须支付金币。
        /// 金币既是击杀产出、又是强化吞噬/局内商店的消费口，后续可直接承接广告。
        /// </summary>
        public static bool ActivateDevour()
        {
            if (Over || Paused) return false;
            if (DevourTimer > 0f || DevourCd > 0f) return false;
            if (Coins < GameConfig.DevourCoinCost) return false;
            Coins -= GameConfig.DevourCoinCost;
            DevourTimer = GameConfig.DevourTime;
            return true;
        }

        /// <summary>金币是否足够开启强化吞噬</summary>
        public static bool DevourReady()
        {
            return Coins >= GameConfig.DevourCoinCost && DevourTimer <= 0f && DevourCd <= 0f;
        }

        /// <summary>可吞噬的最大体积档位：强化期间 +1</summary>
        public static int DevourSizeCap()
        {
            return Stage + (DevourTimer > 0f ? 1 : 0);
        }

        public static float DevourRadius()
        {
            float boost = DevourTimer > 0f ? GameConfig.DevourBoostMul : 1f;
            return GameConfig.DevourRadius * DevourMul * GameConfig.StageScale[Stage] * boost;
        }

        /// <summary>体积压制伤害系数 [策划书 4.1]</summary>
        public static float SizeFactor(int targetSize)
        {
            int diff = Stage - targetSize;
            if (diff > 0) return 1f + Mathf.Min(GameConfig.SizeBonusCap, diff * GameConfig.SizeBonusPerTier);
            if (diff < 0) return GameConfig.SizeWeakFactor;
            return 1f;
        }

        public static void AddCombo()
        {
            Combo += 1;
            ComboTimer = GameConfig.ComboWindow;
            if (Combo > BestCombo) BestCombo = Combo;
            if (Combo > 1 && Combo % 5 == 0)
            {
                AudioKit.PlaySfx(SfxKeys.Combo);
                GameBus.Emit(GameEvents.Combo, Combo);
            }
        }

        public static float ComboExpMul()
        {
            return 1f + Mathf.Min(0.5f, Mathf.Floor(Combo / 5f) * 0.1f);
        }

        public static void AddExp(float value)
        {
            if (Over) return;
            int gain = (int)Mathf.Max(1f, Mathf.Round(value * ExpMul * ComboExpMul()));
            Exp += gain;
            GameBus.Emit(GameEvents.Float, "+" + gain + " EXP");

            while (Exp >= ExpNeed)
            {
                Exp -= ExpNeed;
                Level += 1;
                ExpNeed = NeedFor(Level);

                if (Array.IndexOf(GameConfig.EvolveLevels, Level) >= 0 && Stage < 3)
                {
                    Stage += 1;
                    float ratio = MaxHp > 0 ? Hp / MaxHp : 1f;
                    MaxHp = Mathf.Round(GameConfig.MaxHp * Meta.HpMul() * HpBonusMul * GameConfig.StageHp[Stage]);
                    // 进化按当前血量比例放大，不再免费回满
                    Hp = Mathf.Max(1, Mathf.Round(MaxHp * ratio));
                    // 正面炮不再随进阶自动解锁：只有商店主动加装才显示 dbdp
                    GameBus.Emit(GameEvents.Evolve, Stage);
                    AudioKit.PlaySfx(SfxKeys.Evolve);
                }

                // 升级只发点数，不打断战斗；玩家自己点强化按钮消费
                UpgradePoints += 1;
                GameBus.Emit(GameEvents.LevelUp, Level);
                AudioKit.PlaySfx(SfxKeys.Levelup);
            }
        }

        /// <summary>
        /// 每消灭一个敌人：累计击杀数与金币（无尽生存没有通关配额）。
        /// coinBonus 为敌种 prog 值（敌种表配置），Boss 另算。
        /// </summary>
        public static void AddKill(int coinBonus, bool isBoss)
        {
            if (Over) return;
            Kills += 1;
            Coins += (isBoss ? GameConfig.Survival.BossCoin : GameConfig.Survival.KillCoinBase) + coinBonus;
        }

        /// <summary>玩家死亡：无尽模式只有失败结算，不存在通关</summary>
        public static void Die()
        {
            if (Over) return;
            Over = true;
            AudioKit.PlaySfx(SfxKeys.Lose);
            GameBus.Emit(GameEvents.Result, false);
        }

        /// <summary>
        /// 最终放弃本局（点「重新开始」）时才把局内金币按比例转入局外存档；
        /// 复活不结算，避免重复入账。
        /// </summary>
        public static void SettleRun()
        {
            if (Settled) return;
            Settled = true;
            Meta.Data.gold += Mathf.RoundToInt(Coins * GameConfig.Survival.LoseCoinRatio);
            Meta.Save();
        }

        // ---------------- 复活 ----------------
        /// <summary>是否还能复活：次数未用完、当前处于死亡状态、（配置收费时）金币足够</summary>
        public static bool CanRevive()
        {
            if (!Over) return false;
            if (ReviveUsed >= GameConfig.Survival.ReviveMax) return false;
            return Coins >= GameConfig.Survival.ReviveCoinCost;
        }

        /// <summary>
        /// 复活：保留本局等级/装备/存活时间，恢复一定比例血量并获得短暂无敌；
        /// 是否清空普通兵由配置决定（Boss 始终保留，由 EnemySpawner 监听事件处理）。
        /// </summary>
        public static bool Revive()
        {
            if (!CanRevive()) return false;
            ReviveUsed += 1;
            Coins -= GameConfig.Survival.ReviveCoinCost;
            Hp = Mathf.Max(1f, MaxHp * GameConfig.Survival.ReviveHpRatio);
            ReviveInvincibleT = GameConfig.Survival.ReviveInvincible;
            Invincible = true;
            Over = false;
            Paused = false;
            Combo = 0;
            GameBus.Emit(GameEvents.Revive, GameConfig.Survival.ReviveClearMobs);
            return true;
        }

        /// <summary>存活时间格式化 mm:ss</summary>
        public static string TimeText()
        {
            int total = Mathf.FloorToInt(Elapsed);
            int m = total / 60, s = total % 60;
            return m.ToString("00") + ":" + s.ToString("00");
        }

        public static void Damage(float value)
        {
            if (Over || Paused || Invincible) return;
            Hp -= value;
            AudioKit.PlaySfx(SfxKeys.HitPlayer);
            GameBus.Emit(GameEvents.Hurt, null);
            Combo = 0;
            if (Hp <= 0f)
            {
                Hp = 0f;
                Die();
            }
        }

        public static void Heal(float v)
        {
            Hp = Mathf.Min(MaxHp, Hp + v);
        }

        public static void Tick(float dt)
        {
            if (Over || Paused) return;
            Elapsed += dt;
            if (ReviveInvincibleT > 0f) ReviveInvincibleT -= dt;
            if (ComboTimer > 0f)
            {
                ComboTimer -= dt;
                if (ComboTimer <= 0f) Combo = 0;
            }
            if (MagnetTimer > 0f) MagnetTimer -= dt;

            if (DevourTimer > 0f)
            {
                DevourTimer -= dt;
                if (DevourTimer <= 0f) DevourCd = GameConfig.DevourCd;
            }
            else if (DevourCd > 0f)
            {
                DevourCd -= dt;
            }
        }
    }

    /// <summary>虚拟摇杆输入（对应 LayaAir 版 Joy）</summary>
    public static class Joy
    {
        public static float x = 0f;
        public static float y = 0f;
        public static bool active = false;

        public static void Reset()
        {
            x = 0f; y = 0f; active = false;
        }
    }
}
