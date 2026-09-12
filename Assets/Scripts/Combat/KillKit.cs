using UnityEngine;

namespace BattleFortress
{
    /// <summary>
    /// 击杀奖励统一结算：连击、吞噬音效、经验/金币、死亡特效与震屏、掉落生成。
    /// 敌人实体的销毁/回收由调用方（EnemySpawner）按 Boss 死亡动画需要自行决定，
    /// 本类只负责「打死之后发生什么」，新装备/新杀敌方式都复用这里。
    /// </summary>
    public static class KillKit
    {
        private static int _devourSeq;

        public static void Reward(EnemyUnit e)
        {
            if (e == null) return;
            e.Dead = true;

            GS.AddCombo();
            _devourSeq = (_devourSeq + 1) % 3;
            AudioKit.PlaySfx(_devourSeq == 0 ? SfxKeys.Devour1
                : _devourSeq == 1 ? SfxKeys.Devour2 : SfxKeys.Devour3);

            // Boss 死亡：安排下一只 Boss 的出场时间（配置间隔）；是否真的补由主循环按数量目标判断
            if (e.IsBoss)
            {
                GS.BossWindowStart = GS.Elapsed;
                GS.NextBossTime = GS.Elapsed + GameConfig.Survival.BossInterval;
                AudioKit.PlaySfx(SfxKeys.ExplosionBig);
                GameBus.Emit(GameEvents.Boss, false);
            }

            // 经验/进度统一从 EnemyDef 取（配了浮动区间就随机，Boss 用自身定义）
            float exp = e.Def != null ? e.Def.RollExp() : 0f;
            GS.AddExp(exp);
            GS.AddKill(e.Def != null ? e.Def.Prog : 0, e.IsBoss);

            var ep = e.transform.position;
            // 小怪死亡只播一层爆炸，Boss 才叠满层次
            Fx.PlayVfx(VfxKeys.ExplosionSheet, ep.x, ep.y + 0.9f, ep.z,
                e.IsBoss ? GameConfig.Ai.BossDeathFx : GameConfig.Ai.MobDeathFx);
            if (e.IsBoss)
            {
                Fx.PlayVfx(VfxKeys.DevourRing, ep.x, ep.y + 0.6f, ep.z, GameConfig.Ai.BossDeathRingFx);
                Fx.PlayVfx(VfxKeys.StarSpark, ep.x, ep.y + 1.2f, ep.z, GameConfig.Ai.BossDeathSparkFx);
            }
            Fx.Shake(e.IsBoss ? GameConfig.Ai.BossDeathShake : GameConfig.Ai.MobDeathShake);

            DropSystem.SpawnDrop(ep.x, ep.z, e.Def, e.IsBoss);

            // 非 Boss 立即回池；Boss 有骨骼动画的延迟到 die 播完（由 EnemySpawner 主循环回收），
            // Boss 没有 Animator 时同样立即回池（与原 Devour 行为一致）
            if (!e.IsBoss || e.Animator == null) ObjectPool.Despawn(e.gameObject);
        }
    }
}
