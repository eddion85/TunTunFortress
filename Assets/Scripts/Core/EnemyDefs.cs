namespace BattleFortress
{
    // 敌人/Boss 的种类定义已迁移到 EnemyCatalog.cs（EnemyDef + EnemyCatalog，配置驱动、可扩展）。
    // 本文件只保留拾取物数值与音效/特效资源路径表。

    /// <summary>拾取物数值 [策划书 8 道具]</summary>
    public static class EnemyDefs
    {
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
        public const string TapMarker = "art/vfx/T_UI_TapMarker"; // 点地移动光标
    }
}
