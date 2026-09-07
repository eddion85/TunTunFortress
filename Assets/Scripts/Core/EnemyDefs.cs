namespace BattleFortress
{
    /// <summary>
    /// 敌人种类定义 [策划书 4.4]
    /// </summary>
    public class EnemyKind
    {
        public string key;
        public float hp;
        public float dmg;
        public float exp;
        public int prog;          // 吞噬进度贡献
        public float speed;
        public int size;          // 体积档位，用于体积压制判定
        public bool flee;         // 是否逃跑
        public bool wander;       // 是否游荡（羊）
        public float scale;       // 模型缩放
        public bool ranged;       // 远程（弓箭手）
        public bool flank;        // 绕后（骑兵）
        public float expMin;      // 经验下限（羊）
        public float expMax;      // 经验上限（羊）
    }

    /// <summary>敌人表 / Boss 表 / 拾取物表（由 config.ts 1:1 移植）</summary>
    public static class EnemyDefs
    {
        public static readonly EnemyKind[] KINDS =
        {
            // 羊：HP 1~3、伤害 0、经验 4~8 [策划书 4.4]
            new EnemyKind { key = "sheep",  hp = 3,  dmg = 0, exp = 6,  expMin = 4, expMax = 8,  prog = 2, speed = 3.0f, size = 0, flee = true,  wander = true, scale = 0.9f  },
            // 奶牛 [设计：资源包新增敌种]
            new EnemyKind { key = "cow",    hp = 8,  dmg = 4, exp = 11, prog = 5, speed = 3.3f, size = 1, flee = false, scale = 1.1f  },
            // 农夫：HP 5、伤害 3、经验 7 [策划书 4.4]
            new EnemyKind { key = "farmer", hp = 5,  dmg = 3, exp = 7,  prog = 4, speed = 3.3f, size = 1, flee = false, scale = 1.0f  },
            // 弓箭手：HP 9、伤害 5、经验 14、远程 [策划书 4.4]
            new EnemyKind { key = "archer", hp = 9,  dmg = 5, exp = 14, prog = 6, speed = 3.0f, size = 1, flee = false, scale = 1.0f, ranged = true },
            // 骑兵：HP 14、伤害 7、经验 18、绕后 [策划书 4.4]
            new EnemyKind { key = "rider",  hp = 14, dmg = 7, exp = 18, prog = 8, speed = 4.9f, size = 2, flee = false, scale = 1.15f, flank = true }
        };

        public static EnemyKind KindByKey(string k)
        {
            for (int i = 0; i < KINDS.Length; i++)
                if (KINDS[i].key == k) return KINDS[i];
            return KINDS[0];
        }

        /// <summary>敌种索引，用于 maxTier 解锁判定（顺序同 KINDS）</summary>
        public static int IndexOf(string key)
        {
            switch (key)
            {
                case "sheep": return 0;
                case "cow": return 1;
                case "farmer": return 2;
                case "archer": return 3;
                case "rider": return 4;
                default: return 0;
            }
        }

        // ---------------- Boss [设计：GDD 无细则，按敌人数值放大] ----------------
        public static class Boss
        {
            public const float Hp = 320f;
            public const float Dmg = 14f;
            public const float Exp = 120f;
            public const int Prog = 40;
            public const float Speed = 2.6f;
            public const float Scale = 2.0f;
            public const float SlamCd = 4.5f;
            public const float SlamRadius = 6f;
            public const float Phase2At = 0.5f;   // 血量低于 50% 进入二阶段提速
        }

        // ---------------- 拾取物 [策划书 8 道具] ----------------
        public static class Pickups
        {
            public const float HealthSmall = 20f;
            public const float HealthBig = 45f;
            public const float MagnetTime = 6f;
            public const float MagnetRadius = 14f;
        }
    }

    /// <summary>音效资源路径（相对 Assets/Resources/）</summary>
    public static class SfxKeys
    {
        public const string Devour1 = "art/audio/sfx/sfx_devour_01";
        public const string Devour2 = "art/audio/sfx/sfx_devour_02";
        public const string Devour3 = "art/audio/sfx/sfx_devour_03";
        public const string Levelup = "art/audio/sfx/sfx_levelup";
        public const string Evolve = "art/audio/sfx/sfx_evolve";
        public const string Cannon = "art/audio/sfx/sfx_cannon_fire";
        public const string Arrow = "art/audio/sfx/sfx_arrow_shoot";
        public const string HitEnemy = "art/audio/sfx/sfx_hit_enemy";
        public const string HitPlayer = "art/audio/sfx/sfx_hit_player";
        public const string Coin = "art/audio/sfx/sfx_pickup_coin";
        public const string Health = "art/audio/sfx/sfx_pickup_health";
        public const string Magnet = "art/audio/sfx/sfx_magnet_loop";
        public const string Dash = "art/audio/sfx/sfx_dash";
        public const string CardPick = "art/audio/sfx/sfx_ui_card_pick";
        public const string Click = "art/audio/sfx/sfx_ui_click";
        public const string Combo = "art/audio/sfx/sfx_combo_up";
        public const string ExplosionBig = "art/audio/sfx/sfx_explosion_big";
        public const string ExplosionSmall = "art/audio/sfx/sfx_explosion_small";
        public const string BossAppear = "art/audio/sfx/sfx_boss_appear";
        public const string LowHp = "art/audio/sfx/sfx_low_hp_loop";
        public const string Win = "art/audio/sfx/sfx_win";
        public const string Lose = "art/audio/sfx/sfx_lose";
    }

    public static class BgmKeys
    {
        public const string Farm = "art/audio/bgm/bgm_farm_loop";
        public const string Battle = "art/audio/bgm/bgm_battle_loop";
        public const string Boss = "art/audio/bgm/bgm_boss_loop";
        public const string Result = "art/audio/bgm/bgm_result";
    }

    /// <summary>特效贴图路径（相对 Assets/Resources/）</summary>
    public static class VfxKeys
    {
        public const string HitSheet = "art/vfx/T_FX_Hit_Sheet";
        public const string ExplosionSheet = "art/vfx/T_FX_Explosion_Sheet";
        public const string EvolveSheet = "art/vfx/T_FX_Evolve_Sheet";
        public const string DevourRing = "art/vfx/T_FX_DevourRing";
        public const string RingWave = "art/vfx/T_FX_RingWave";
        public const string FireBall = "art/vfx/T_FX_FireBall";
        public const string SmokePuff = "art/vfx/T_FX_SmokePuff";
        public const string StarSpark = "art/vfx/T_FX_StarSpark";
        public const string SpeedLine = "art/vfx/T_FX_SpeedLine";
        public const string SoftCircle = "art/vfx/T_FX_SoftCircle";
        public const string LightBeam = "art/vfx/T_FX_LightBeam";
        public const string DebrisWood = "art/vfx/T_FX_DebrisWood";
        public const string ShadowBlob = "art/vfx/T_FX_ShadowBlob";
    }
}
